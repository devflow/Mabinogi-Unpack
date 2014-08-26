using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ilpack
{
    public class ItemInfo
    {
        public int index { get; set; }
        public long pos { get; set;  }
        public String name { get; set; }
        public UInt32 seed { get; set; }   //version
        public UInt32 zero { get; set; }
        public UInt32 offset { get; set; }
        public UInt32 compress_size { get; set; }
        public UInt32 decompress_size { get; set; }
        public UInt32 is_compressed { get; set; } // 0 , 1
        public UInt32 skip_filetime_40bytes { get; set; } // filetimes

        //// 여기부터는 팩에 있지 않은 일팩전용 메서드/속성들임

        /// <summary>
        /// 해당 item이 언팩을 위해 숨겨졌으면 true 아님 false
        /// </summary>
        public bool isHidden()
        { 
            //주의할게, 딴팩은 모르겠지만 어쨋튼 나비팩은 모든 .을 *로 치환함. 1개 치환하는게 아님 모두 치환해야함.
            return (name.IndexOf('*') >= 0);

        }
    }
    /// <summary>
    /// Package 클래스는 불필요하다고 생각하는 것을 맘대로 잘래냈으므로, 업데이트로 정상작동 안할 수 있음.
    /// 또한, 다른 특정 시스템에선 테스트를 하지않음.
    /// 
    ///  특히 압축되지 않은 파일을 불러오는 함수를 만들지 않음.
    /// 
    /// 기준 140209 / 클라이언트 버전755 / 윈8.1
    /// </summary>
    public class Package
    {
        public String PackagePath { set; get; }

       
        public static byte[] SIGNATURE = { 0x50, 0x41, 0x43, 0x4b, 0x02, 0x01, 0x00, 0x00 }; // P A C K x02 x01 x0 x0

        //Package Header
        public UInt32 version;           // 팩키지버전
        public UInt32 sum;               // 파일의 수
        //FILETIME ft1;
        //FILETIME ft2;
        //public char[] path ; //data\

        //ListHeader
        public UInt32 sum_lh;            // 파일의 수
        public UInt32 list_header_size;  // 파일리스트헤더 사이즈 (inc blank)
        public UInt32 blank_size;        // 데이터와 헤더 사이 빈공간.
        public UInt32 data_section_size; // 데이터섹션의크기
        public byte[] zero_blank = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };


        public List<ItemInfo> packageItems; 

        private long ListSectionOffset = 0;

        private FileStream _openFile(bool isSparse)
        {
            if (isSparse)
            {
                FileStream t = new FileStream(PackagePath, FileMode.Open);
                MarkAsSparseFile(t.SafeFileHandle);
                return t;
            }
            else
                return new FileStream(PackagePath, FileMode.Open);
        }

        public bool LoadPackage(String path)
        {
            packageItems = new List<ItemInfo>();
            byte[] t4byte = { 0, 0, 0, 0 };
            byte[] t2byte = { 0, 0 };
            byte[] t8byte = { 0, 0, 0, 0, 0, 0, 0, 0 };
            byte[] t16byte = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            FileStream fs = null;
            try
            {
                this.PackagePath = path;

                fs = _openFile(true);

                fs.Read(t8byte, 0, 8);
                if (!t8byte.SequenceEqual(SIGNATURE))
                    throw new Exception("올바른 Package파일이 아닙니다.");

                fs.Read(t4byte, 0, 4);
                version = BitConverter.ToUInt32(t4byte, 0);

                fs.Read(t4byte, 0, 4);
                sum = BitConverter.ToUInt32(t4byte, 0);

                //Skip 16byte for f1,f2;
                fs.Seek(16, SeekOrigin.Current);

                //Skip 480byte for path (/data .... )
                fs.Seek(480, SeekOrigin.Current);

                fs.Read(t4byte, 0, 4);
                sum_lh = BitConverter.ToUInt32(t4byte, 0);

                fs.Read(t4byte, 0, 4);
                list_header_size = BitConverter.ToUInt32(t4byte, 0);

                fs.Read(t4byte, 0, 4);
                blank_size = BitConverter.ToUInt32(t4byte, 0);

                fs.Read(t4byte, 0, 4);
                data_section_size = BitConverter.ToUInt32(t4byte, 0);

                
                fs.Read(t16byte,0,16);
                
                if(!t16byte.SequenceEqual(zero_blank))
                    throw new Exception("Package구조가 슨상되었습니다.");

                //All Success.

            }
            catch (Exception err_excep)
            {
                Program.err.Log("LoadPackage@Package.class", err_excep.Message + ";err:" + err_excep.StackTrace);
                fs.Close();
                return false;
            }

            ListSectionOffset = fs.Position;
            fs.Close();
            return true;
        }

        public delegate void publishProgress(int cnt, string msg);
        public delegate void publishItem(ItemInfo ii);

        public List<ItemInfo> getItems(publishItem pp)
        {
            /*
             *   이부분 을 수정하려면 getitems는 여러개이므로 꼭 수정해야뎀.
             */

            List<ItemInfo> nitems = new List<ItemInfo>();
            FileStream fs = null;

            try
            {
                fs = _openFile(true);

                fs.Seek(ListSectionOffset, SeekOrigin.Begin);

                for (int i = 0; i < sum_lh; i++)
                {
                    ItemInfo cItem = new ItemInfo();
                    cItem.index = i;

                    byte[] t4byte = { 0, 0, 0, 0 };
                    int lSize;
                    int len_or_type = 0;

                    len_or_type = fs.ReadByte();

                    if (len_or_type < 4) //3줄 이하
                    {
                        lSize = (16 * len_or_type) + 15;
                    }
                    else if (len_or_type == 4) //4줄
                    {
                        lSize = 0x60 - 1;
                    }
                    else //5줄 이상일때는 길이를 준다..
                    {
                        fs.Read(t4byte, 0, 4);
                        lSize = unchecked((int)BitConverter.ToUInt32(t4byte, 0));
                    }

                    cItem.pos = fs.Position;

                    byte[] b_name = new byte[lSize];

                    fs.Read(b_name, 0, lSize);

                    cItem.name = ASCIIEncoding.ASCII.GetString(b_name).Replace("\0", String.Empty);


                    fs.Read(t4byte, 0, 4);
                    cItem.seed = BitConverter.ToUInt32(t4byte, 0);

                    fs.Read(t4byte, 0, 4);
                    cItem.zero = BitConverter.ToUInt32(t4byte, 0);

                    fs.Read(t4byte, 0, 4);
                    cItem.offset = BitConverter.ToUInt32(t4byte, 0);
                    cItem.offset += 544 + list_header_size;

                    fs.Read(t4byte, 0, 4);
                    cItem.compress_size = BitConverter.ToUInt32(t4byte, 0);

                    fs.Read(t4byte, 0, 4);
                    cItem.decompress_size = BitConverter.ToUInt32(t4byte, 0);

                    fs.Read(t4byte, 0, 4);
                    cItem.is_compressed = BitConverter.ToUInt32(t4byte, 0);

                    //skip to 40bytes
                    fs.Seek(40, SeekOrigin.Current);

                    nitems.Add(cItem);

                    //if(i%20==0) 여기서 처리함 안된다.
                    pp(cItem);

                }

            }
            catch (Exception err_excep)
            {
                Program.err.Log("getItems@Package.class", err_excep.Message + ";err:" + err_excep.StackTrace);
                fs.Close();
                return null;
            }

            fs.Close();
            return nitems;
        }
        public List<ItemInfo> getItems(publishProgress pp)
        {
            /*
             *   이부분 을 수정하려면 getitems는 여러개이므로 꼭 수정해야뎀.
             */

            List<ItemInfo> nitems = new List<ItemInfo>();
            FileStream fs = null;

            try
            {
                fs = _openFile(true);

                fs.Seek(ListSectionOffset, SeekOrigin.Begin);

                for (int i = 0; i < sum_lh; i++)
                {
                    ItemInfo cItem = new ItemInfo();
                    cItem.index = i;

                    byte[] t4byte = { 0, 0, 0, 0 };
                    int lSize;
                    int len_or_type = 0;

                    len_or_type = fs.ReadByte();

                    if (len_or_type < 4) //3줄 이하
                    {
                        lSize = (16 * len_or_type) + 15;
                    }
                    else if (len_or_type == 4) //4줄
                    {
                        lSize = 0x60 - 1;
                    }
                    else //5줄 이상일때는 길이를 준다..
                    {
                        fs.Read(t4byte, 0, 4);
                        lSize = unchecked((int)BitConverter.ToUInt32(t4byte, 0));
                    }

                    cItem.pos = fs.Position;

                    byte[] b_name = new byte[lSize];

                    fs.Read(b_name, 0, lSize);

                    cItem.name = ASCIIEncoding.ASCII.GetString(b_name).Replace("\0", String.Empty);


                    fs.Read(t4byte, 0, 4);
                    cItem.seed = BitConverter.ToUInt32(t4byte, 0);

                    fs.Read(t4byte, 0, 4);
                    cItem.zero = BitConverter.ToUInt32(t4byte, 0);

                    fs.Read(t4byte, 0, 4);
                    cItem.offset = BitConverter.ToUInt32(t4byte, 0);
                    cItem.offset += 544 + list_header_size;

                    fs.Read(t4byte, 0, 4);
                    cItem.compress_size = BitConverter.ToUInt32(t4byte, 0);

                    fs.Read(t4byte, 0, 4);
                    cItem.decompress_size = BitConverter.ToUInt32(t4byte, 0);

                    fs.Read(t4byte, 0, 4);
                    cItem.is_compressed = BitConverter.ToUInt32(t4byte, 0);

                    //skip to 40bytes
                    fs.Seek(40, SeekOrigin.Current);

                    nitems.Add(cItem);
                    pp(i, cItem.name);

                }

            }
            catch (Exception err_excep)
            {
                Program.err.Log("getItems@Package.class", err_excep.Message + ";err:" + err_excep.StackTrace);
                fs.Close();
                return null;
            }

            fs.Close();
            return nitems;
        }

        public bool getDecompressedContent(ItemInfo citem, String save_path)
        {
            FileStream fs = null;

            try
            {
                fs = _openFile(true);
                fs.Seek(citem.offset,SeekOrigin.Begin);

                byte[] cmpressed_data = new byte[citem.compress_size];

                int tmp = fs.Read(cmpressed_data, 0, (int)citem.compress_size);

                if (tmp != citem.compress_size)
                    throw new Exception("not correct reading size.");

                Encryption.Decrypt(cmpressed_data, (int)citem.seed);

                MemoryStream cmpsd_data = new MemoryStream(cmpressed_data);
                cmpsd_data.Position = 2;

                DeflateStream ds = new DeflateStream(cmpsd_data, CompressionMode.Decompress);
                MemoryStream ms = new MemoryStream();

                ds.CopyTo(ms);

                byte[] decompressed_data = new byte[citem.decompress_size];
                decompressed_data = ms.ToArray();

                if(decompressed_data.Length != citem.decompress_size)
                    throw new Exception("fail to decompressing file.");

                FileStream writeStream = new FileStream(save_path, FileMode.Create);

                writeStream.Write(decompressed_data, 0, decompressed_data.Length);
    
                writeStream.Close();
    

            }
            catch (Exception err_Excep)
            {
                Program.err.Log("getDecompressedContent@Package.class", err_Excep.Message + ";err:" + err_Excep.StackTrace);
                fs.Close();
                return false;
            }

            fs.Close();
            return true;

        }

        public void doApplyItem(ItemInfo cItem, bool doHide)
        {
            FileStream fs = null;
            try
            {
                fs = _openFile(false);
                fs.Seek(cItem.pos,SeekOrigin.Begin);

                byte[] changedName = ASCIIEncoding.ASCII.GetBytes(cItem.name.Replace(doHide ? '.' : '*', doHide ? '*' : '.'));

                fs.Write(changedName, 0, changedName.Length);

                fs.Close();
            }

            catch (Exception err_Excep)
            {
                Program.err.Log("hideitem@Package.class", err_Excep.Message + ";err:" + err_Excep.StackTrace);
                fs.Close();
                return;
            }
            fs.Close();
            return;
        }



        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            int dwIoControlCode,
            IntPtr InBuffer,
            int nInBufferSize,
            IntPtr OutBuffer,
            int nOutBufferSize,
            ref int pBytesReturned,
            [In] ref NativeOverlapped lpOverlapped
        );

        static void MarkAsSparseFile(SafeFileHandle fileHandle)
        {
            int bytesReturned = 0;
            NativeOverlapped lpOverlapped = new NativeOverlapped();
            bool result =
                DeviceIoControl(
                    fileHandle,
                    590020,   
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero,
                    0,
                    ref bytesReturned,
                    ref lpOverlapped);
            if (result == false)
                throw new Win32Exception();
        }
    }
}
