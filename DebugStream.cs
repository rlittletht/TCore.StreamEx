

using System;
using System.Dynamic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace TCore.Debug
{
    class DebugStream : Stream
    {
        private byte[] m_rgbData;
        private long m_cbStream;
        private long m_ibStreamMac;
        private long m_ibCur;

        public DebugStream()
        {
            m_ibStreamMac = m_ibCur = 0;
            m_rgbData = new byte[1024];
            m_cbStream = 1024;
        }

        void EnsureBufferSize(long ibStart, long cbWrite, bool fInit)
        {
            if (m_cbStream < ibStart + cbWrite)
            {
                byte[] rgbSav = m_rgbData;
                long cbNew = ibStart + cbWrite + 4096;
                m_rgbData = new byte[cbNew];

                m_cbStream = cbNew;
                rgbSav.CopyTo(m_rgbData, 0);
            }

            if (m_ibStreamMac < ibStart)
            {
                if (fInit)
                {
                    while (m_ibStreamMac <= ibStart)
                    {
                        m_rgbData[m_ibStreamMac] = 0;
                        m_ibStreamMac++;
                    }
                }
                else
                {
                    m_ibStreamMac = ibStart;
                }
            }
        }

        public void WriteTestDataAt(long ibTestStart, byte[] rgbWrite)
        {
            int cbWrite = rgbWrite?.Length ?? 0;

            EnsureBufferSize(ibTestStart, cbWrite, true);
            Seek(ibTestStart, SeekOrigin.Begin);
            if (rgbWrite != null)
                Write(rgbWrite, 0, cbWrite);
        }

        public override bool CanRead => throw new NotImplementedException();
        public override bool CanSeek => throw new NotImplementedException();
        public override bool CanWrite => throw new NotImplementedException();
        public override long Length => m_ibStreamMac;
        public override long Position
        {
            get { return m_ibCur; }
            set { Seek(value, SeekOrigin.Begin); }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    m_ibCur = offset;
                    return m_ibCur;
                case SeekOrigin.Current:
                    m_ibCur = Math.Min(m_ibCur + offset, m_ibStreamMac);
                    return m_ibCur;
                case SeekOrigin.End:
                    m_ibCur = Math.Min(m_ibStreamMac + offset, m_ibStreamMac);
                    return m_ibCur;
            }

            throw new InvalidDataException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (m_ibCur >= m_ibStreamMac)
                return 0;

            long lcbToRead = Math.Min(count, m_ibStreamMac - m_ibCur);
            int cbToRead = (int) lcbToRead;

            if (cbToRead != lcbToRead)
                throw new OverflowException();

            if (cbToRead > 0)
            {
                Array.Copy(m_rgbData, m_ibCur, buffer, offset, cbToRead);
                m_ibCur += cbToRead;
            }

            return cbToRead;
        }

        /*----------------------------------------------------------------------------
            %%Function: SetLength
            %%Qualified: TCore.ListenAz.Stage2.ListenSyncFile.Buffer.DebugStream.SetLength
            %%Contact: rlittle
    
            Set ibStreamMac (filling uninit data to 0 if we are stretching)
    
            this won't shrink the allocated memory though
        ----------------------------------------------------------------------------*/
        public override void SetLength(long value)
        {
            EnsureBufferSize(value, 0, true);

            m_ibStreamMac = value;
        }

        void EnsureMacAdjusted(int count)
        {
            while (m_ibStreamMac < m_ibCur + count)
            {
                m_rgbData[m_ibStreamMac++] = 0;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureBufferSize(m_ibCur, count, true);
            EnsureMacAdjusted(count);

            Array.Copy(buffer, offset, m_rgbData, m_ibCur, count);
            
            m_ibCur += count;
        }

        static DebugStream StmInit(string sInit)
        {
            DebugStream stm = new DebugStream();
            stm.Write(Encoding.UTF8.GetBytes(sInit), 0, sInit.Length);

            return stm;
        }
        [Test]
        public static void TestStreamWriteBasicWrite()
        {
            DebugStream stm = StmInit("test");

            Assert.AreEqual(4, stm.Length);
            Assert.AreEqual(4, stm.Position);
            Assert.AreEqual(4, stm.Seek(0, SeekOrigin.End));
        }

        [Test]
        public static void TestStreamReadBasicRead()
        {
            DebugStream stm = StmInit("test");
            stm.Seek(0, SeekOrigin.Begin);

            byte[] rgb = new byte[4];

            Assert.AreEqual(4, stm.Read(rgb, 0, 4));
            Assert.AreEqual(Encoding.UTF8.GetBytes("test"), rgb);
        }

        [Test]
        public static void TestStreamSeek()
        {
            DebugStream stm = StmInit("01234");

            Assert.AreEqual(5, stm.Seek(0, SeekOrigin.Current));
            Assert.AreEqual(5, stm.Seek(0, SeekOrigin.End));
            byte[] rgb = new byte[1];

            Assert.AreEqual(0, stm.Read(rgb, 0, 1));

            Assert.AreEqual(0, stm.Seek(0, SeekOrigin.Begin));
            Assert.AreEqual(1, stm.Read(rgb, 0, 1));
            Assert.AreEqual(1, stm.Seek(0, SeekOrigin.Current));
            Assert.AreEqual(Encoding.UTF8.GetBytes("0"), rgb);

            Assert.AreEqual(4, stm.Seek(-1, SeekOrigin.End));
            Assert.AreEqual(1, stm.Read(rgb, 0, 1));
            Assert.AreEqual(5, stm.Seek(0, SeekOrigin.Current));
            Assert.AreEqual(Encoding.UTF8.GetBytes("4"), rgb);

            Assert.AreEqual(3, stm.Seek(-2, SeekOrigin.Current));
            Assert.AreEqual(1, stm.Read(rgb, 0, 1));
            Assert.AreEqual(4, stm.Seek(0, SeekOrigin.Current));
            Assert.AreEqual(Encoding.UTF8.GetBytes("3"), rgb);
        }

        [Test]
        public static void TestStreamWrite_SeekBack_WriteGrowFile() // check that length and position are correct
        {
            DebugStream stm = StmInit("0123456789");

            stm.Seek(-2, SeekOrigin.Current);

            byte[] rgbWrite = new byte[3] {65, 66, 67};
            stm.Write(rgbWrite, 0, 3);

            Assert.AreEqual(11, stm.Position);
            Assert.AreEqual(11, stm.Length);
            stm.Position = 0;

            byte[] rgbExpected= Encoding.UTF8.GetBytes("01234567ABC");
            byte[] rgbRead = new byte[11];
            Assert.AreEqual(11, stm.Read(rgbRead, 0, 11));
            Assert.AreEqual(rgbExpected, rgbRead);
        }

        [Test]
        public static void TestStreamWrite_SeekBack_WriteNoGrowFile()
        {
            DebugStream stm = StmInit("0123456789");

            stm.Seek(-4, SeekOrigin.Current);

            byte[] rgbWrite = new byte[2] { 65, 66 };
            stm.Write(rgbWrite, 0, 2);

            Assert.AreEqual(8, stm.Position);
            Assert.AreEqual(10, stm.Length);
            stm.Position = 0;

            byte[] rgbExpected = Encoding.UTF8.GetBytes("012345AB89");
            byte[] rgbRead = new byte[10];
            Assert.AreEqual(10, stm.Read(rgbRead, 0, 10));
            Assert.AreEqual(rgbExpected, rgbRead);
        }

        [Test]
        public static void TestStreamWrite_SetLengthNoChangeFile() // check length and position
        {
            DebugStream stm = StmInit("0123456789");

            stm.SetLength(10);
            Assert.AreEqual(10, stm.Length);

            byte[] rgbExpected = Encoding.UTF8.GetBytes("0123456789");
            byte[] rgbRead = new byte[10];
            stm.Position = 0;

            Assert.AreEqual(10, stm.Read(rgbRead, 0, 10));
            Assert.AreEqual(rgbExpected, rgbRead);
        }

        [Test]
        public static void TestStreamWrite_SetLengthGrowFile() // check length and position
        {
            DebugStream stm = StmInit("0123456789");

            stm.SetLength(20);
            Assert.AreEqual(10, stm.Position);
            Assert.AreEqual(20, stm.Length);

            stm.Seek(-4, SeekOrigin.Current);

            byte[] rgbWrite = new byte[2] { 65, 66 };
            stm.Write(rgbWrite, 0, 2);

            Assert.AreEqual(8, stm.Position);
            Assert.AreEqual(20, stm.Length);
            stm.Position = 0;

            byte[] rgbExpected = Encoding.UTF8.GetBytes("012345AB89");
            byte[] rgbRead = new byte[10];
            Assert.AreEqual(10, stm.Read(rgbRead, 0, 10));
            Assert.AreEqual(rgbExpected, rgbRead);
        }

        [Test]
        public static void TestStreamSetLength_WriteIntoSpaceNoGrow()
        {
            DebugStream stm = new DebugStream();
            stm.SetLength(20);

            Assert.AreEqual(0, stm.Position);
            Assert.AreEqual(20, stm.Length);

            byte[] rgbWrite = new byte[2] { 65, 66 };
            stm.Write(rgbWrite, 0, 2);

            Assert.AreEqual(2, stm.Position);
            Assert.AreEqual(20, stm.Length);
            stm.Position = 0;

            byte[] rgbRead = new byte[2];
            Assert.AreEqual(2, stm.Read(rgbRead, 0, 2));
            Assert.AreEqual(rgbWrite, rgbRead);
        }

        [Test]
        public static void TestStreamSetLength_SeekIntoSpace_WriteIntoSpaceNoGrow()
        {
            DebugStream stm = new DebugStream();
            stm.SetLength(20);
            stm.Position = 10;
            Assert.AreEqual(10, stm.Position);
            Assert.AreEqual(20, stm.Length);

            byte[] rgbWrite = new byte[2] { 65, 66 };
            stm.Write(rgbWrite, 0, 2);

            Assert.AreEqual(12, stm.Position);
            Assert.AreEqual(20, stm.Length);
            stm.Position = 10;

            byte[] rgbRead = new byte[2];
            Assert.AreEqual(2, stm.Read(rgbRead, 0, 2));
            Assert.AreEqual(rgbWrite, rgbRead);
        }

        [Test]
        public static void TestStreamSetLength_SeekIntoSpace_WriteIntoSpaceGrow()
        {
            DebugStream stm = new DebugStream();
            stm.SetLength(20);
            stm.Position = 18;
            Assert.AreEqual(18, stm.Position);
            Assert.AreEqual(20, stm.Length);

            byte[] rgbWrite = new byte[6] { 65, 66, 67, 68, 69, 70 };
            stm.Write(rgbWrite, 0, 6);

            Assert.AreEqual(24, stm.Position);
            Assert.AreEqual(24, stm.Length);
            stm.Position = 18;

            byte[] rgbRead = new byte[6];
            Assert.AreEqual(6, stm.Read(rgbRead, 0, 6));
            Assert.AreEqual(rgbWrite, rgbRead);
        }

        [Test]
        public static void TestStreamSeekBeyondEnd()
        {
            DebugStream stm = new DebugStream();
            stm.Position = 2;
            Assert.AreEqual(2, stm.Position);
            Assert.AreEqual(0, stm.Length);
        }

        [Test]
        public static void TestStreamSeekBeyondEnd_Write()
        {
            DebugStream stm = new DebugStream();
            stm.Position = 2;
            Assert.AreEqual(2, stm.Position);
            Assert.AreEqual(0, stm.Length);

            byte[] rgbWrite = new byte[6] { 65, 66, 67, 68, 69, 70 };
            stm.Write(rgbWrite, 0, 6);

            Assert.AreEqual(8, stm.Position);
            Assert.AreEqual(8, stm.Length);
            stm.Position = 0;

            byte[] rgbRead = new byte[8];
            Assert.AreEqual(8, stm.Read(rgbRead, 0, 8));
            byte[] rgbExpected = {0, 0, 65, 66, 67, 68, 69, 70};

            Assert.AreEqual(rgbExpected, rgbRead);
        }

        [Test]
        // check length and position (position will be in the truncated space. without a write, this leaves thile file trunaceted)
        public static void TestStreamWrite_SetLengthTruncate()
        {
            DebugStream stm = StmInit("0123456789");

            stm.SetLength(5);
            Assert.AreEqual(10, stm.Position);
            Assert.AreEqual(5, stm.Length);

            byte[] rgbExpected = Encoding.UTF8.GetBytes("01234");
            byte[] rgbRead = new byte[5];
            // try to read from beyond the end
            Assert.AreEqual(0, stm.Read(rgbRead, 0, 5));
            //now reposition and read
            stm.Position = 0;
            Assert.AreEqual(5, stm.Read(rgbRead, 0, 5));

            Assert.AreEqual(rgbExpected, rgbRead);
        }

        [Test]
        public static void TestStreamWrite_SetLengthTruncate_Write()
        {
            DebugStream stm = StmInit("0123456789");

            stm.SetLength(5);
            Assert.AreEqual(10, stm.Position);
            Assert.AreEqual(5, stm.Length);

            byte[] rgbWrite = new byte[2] { 65, 66 };
            stm.Write(rgbWrite, 0, 2);

            Assert.AreEqual(12, stm.Position);
            Assert.AreEqual(12, stm.Length);
            stm.Position = 0;

            byte[] rgbRead = new byte[12];
            Assert.AreEqual(12, stm.Read(rgbRead, 0, 12));

            byte[] rgbExpected = new byte[] { 48, 49, 50, 51, 52, 0, 0, 0, 0, 0, 65, 66};
            
            Assert.AreEqual(rgbExpected, rgbRead);
        }

        [Test]
        public static void
            TestStreamWriteBeyondInitialAllocatedBuffer() // check that we reallocated teh buffer and retained its contents correctly
        {
            DebugStream stm = new DebugStream();
            stm.Position = 1020;

            Assert.AreEqual(1024, stm.m_cbStream);
            byte[] rgbWrite = Encoding.UTF8.GetBytes("0123456789");
            stm.Write(rgbWrite, 0, 10);
            Assert.LessOrEqual(1024 + 4096, stm.m_cbStream);
            stm.Position = 1020;
            byte[] rgbRead = new byte[10];

            Assert.AreEqual(10, stm.Read(rgbRead, 0, 10));
            Assert.AreEqual(rgbWrite, rgbRead);
        }

        [Test]
        public static void
            TestStreamWrite_WriteBeyondInitialAllocatedBuffer() // check that we reallocated teh buffer and retained its contents correctly
        {
            DebugStream stm = new DebugStream();
            byte[] rgbWrite = Encoding.UTF8.GetBytes("0123456789");

            // write at the beginning (to make sure its retained)
            stm.Write(rgbWrite, 0, 10);

            stm.Position = 1020;

            Assert.AreEqual(1024, stm.m_cbStream);
            stm.Write(rgbWrite, 0, 10);

            Assert.LessOrEqual(1024 + 4096, stm.m_cbStream);
            stm.Position = 1020;
            byte[] rgbRead = new byte[10];

            Assert.AreEqual(10, stm.Read(rgbRead, 0, 10));
            Assert.AreEqual(rgbWrite, rgbRead);

            // and now lets make sure the initial write has been preserved
            stm.Position = 0;

            Assert.AreEqual(10, stm.Read(rgbRead, 0, 10));
            Assert.AreEqual(rgbWrite, rgbRead);
        }

        [Test]
        public static void TestStreamWriteToLimitOfAllocBuffer_WriteAgainToGrowBuffer()
        {
            DebugStream stm = new DebugStream();
            byte[] rgbWrite = Encoding.UTF8.GetBytes("0123456789");

            // write at the very end (but not over)
            stm.Position = 1014;
            stm.Write(rgbWrite, 0, 10);

            Assert.AreEqual(1024, stm.m_cbStream);
            stm.Write(rgbWrite, 0, 10);

            Assert.LessOrEqual(1024 + 4096, stm.m_cbStream);
            stm.Position = 1014;
            byte[] rgbRead = new byte[10];

            Assert.AreEqual(10, stm.Read(rgbRead, 0, 10));
            Assert.AreEqual(rgbWrite, rgbRead);

            // and now lets make sure the initial write has been preserved
            stm.Position = 1024;

            Assert.AreEqual(10, stm.Read(rgbRead, 0, 10));
            Assert.AreEqual(rgbWrite, rgbRead);
        }

        [Test]
        public static void TestStreamSetLengthBeyondInitialAllocatedBuffer_WriteBeyondInitialAllocatedBuffer()
        {
            DebugStream stm = new DebugStream();
            byte[] rgbWrite = Encoding.UTF8.GetBytes("0123456789");

            // write at the very end (but not over)
            stm.Position = 2048;

            Assert.AreEqual(0, stm.Length);
            Assert.AreEqual(1024, stm.m_cbStream);
            Assert.AreEqual(2048, stm.Position);

            stm.Write(rgbWrite, 0, 10);

            Assert.LessOrEqual(1024 + 4096, stm.m_cbStream);
            Assert.AreEqual(2058, stm.Length);
            Assert.AreEqual(2058, stm.Position);

            stm.Position = 2048;
            byte[] rgbRead = new byte[10];

            Assert.AreEqual(10, stm.Read(rgbRead, 0, 10));
            Assert.AreEqual(rgbWrite, rgbRead);
        }


    }
}