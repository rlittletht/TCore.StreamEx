

using System;
using System.Text;
using System.Xml;
using NUnit.Framework;
using TCore.Debug;

namespace TCore.StreamEx
{
    public partial class BufferedStreamEx
    {
        // things we want to test:
        public BufferedStreamEx() { }   // only for NUnit

        static void RunTestExpectingException(TestDelegate pfn, string sExpectedException)
        {
            if (sExpectedException == "System.Xml.XmlException")
                Assert.Throws<XmlException>(pfn);
            else if (sExpectedException == "System.Exception")
                Assert.Throws<Exception>(pfn);
            else if (sExpectedException == "System.OverflowException")
                Assert.Throws<OverflowException>(pfn);
            else if (sExpectedException == "System.ArgumentException")
                Assert.Throws<ArgumentException>(pfn);
            else if (sExpectedException == "System.FormatException")
                Assert.Throws<FormatException>(pfn);
            else if (sExpectedException != null)
                throw new Exception("unknown exception type");
        }
        // (remember, when we create the buffered stream, we can start
        // whenever we want and artifically limit the end to wherever we
        // want.  (basically, treat a contiguous region of the file as
        // the file)

        // ReadLine

        // we will simulate all of this with 5 character buffers (to make the test cases managable!)

        // (REMEMBER: \n turns into 0x0d0x0a (so its 2 bytes)
        // * line at start of buffer, ends before buffer swap
        [TestCase("0\n", 0, 3, 5, new string[] {"0"}, 3, null )]
        [TestCase("0\x240d\x240a", 0, 3, 5, new string[] { "0" }, 3, null)]
        [TestCase("0\x240a", 0, 2, 5, new string[] { "0" }, 2, null)]
        // * line not at start of buffer, ends before buffer swap
        [TestCase("01\n", 1, 4, 5, new string[] { "1" }, 4, null)]
        // * line at start, then line spanning buffer before end of 2nd buffer - SHOULD TRUNCATE
        [TestCase("01234567\n", 0, 10, 5, new string[] { "01234", "567" }, 10, null)]
        // * line at start, then line ending at end of buffer
        [TestCase("012\n", 0, 5, 5, new string[] { "012" }, 5, null)]   // 012 + CR + LF
        // * line at start, then line ending at end of buffer - 1
        [TestCase("01\n", 0, 4, 5, new string[] { "01" }, 4, null)]   // 01 + CR + LF
        // * line at start, then line ending at end of buffer - 2
        [TestCase("0\n", 0, 3, 5, new string[] { "0" }, 3, null)]   // 0 + CR + LF
        // * line at start, then line ending at end of buffer + 1 (swaps buffer, then stops with the first char after the swap) (TRUNCATE)
        [TestCase("0123\n", 0, 6, 5, new string[] { "0123\r", "" }, 6, null)]
        // * line at start, then line ending at end of buffer + 2 (swaps buffer, then stops with the first char after the swap)
        [TestCase("01234\n", 0, 7, 5, new string[] { "01234", "" }, 7, null)]

        // * line at start, then line ending before end of buffer, then line spanning buffer and ending before end of next buffer (and not overflow token size)
        [TestCase("0\n345\n", 0, 8, 5, new string[] { "0", "345" }, 8, null)]
        // * line at start, then line ending at end of buffer, then line ending before end of buffer
        [TestCase("012\n5\n", 0, 8, 5, new string[] { "012", "5" }, 8, null)]
        // * line at start, then line ending at end of buffer - 1, then line ending before end of buffer
        [TestCase("01\n45\n", 0, 8, 5, new string[] { "01", "45" }, 8, null)]
        // * line at start, then line ending at end of buffer - 2, then line ending before end of buffer
        [TestCase("0\n345\n", 0, 8, 5, new string[] { "0", "345" }, 8, null)]
        // * line at start, then line ending at end of buffer + 1 (swaps buffer (TRUNCATES), then consumes the separated part of the CRLF), then line ending before end of buffer
        [TestCase("0123\n67\n", 0, 10, 5, new string[] { "0123\r", "", "67" }, 10, null)]
        // * line not at start, then line ending at end of buffer + 1 (swaps buffer, then stops with the first char after the swap), then line ending before end of buffer
        [TestCase("0123\n67\n", 2, 10, 5, new string[] { "23", "67" }, 10, null)]
        // * line at start, then line ending at end of buffer + 2 (swaps buffer (TRUNCATES), then consumes the entire CRLF), then line ending before end of buffer
        [TestCase("01234\n78\n", 0, 11, 5, new string[] { "01234", "", "78" }, 11, null)]
        // * line not at start, then line ending at end of buffer + 2 (swaps buffer, then stops with the first char after the swap), then line ending before end of buffer
        [TestCase("01234\n78\n", 2, 11, 5, new string[] { "234", "78" }, 11, null)]
        // * line at start, then line ending at end of buffer, then line ending at end of buffer
        [TestCase("012\n567\n", 0, 10, 5, new string[] { "012", "567" }, 10, null)]
        // * line at start, then line ending at end of buffer - 1, then line ending at end of buffer (TRUNCATE)
        [TestCase("01\n4567\n", 0, 10, 5, new string[] { "01", "4567\r", "" }, 10, null)]
        // * line at start, then line ending at end of buffer - 2, then line ending at end of buffer (TRUNCATE)
        [TestCase("0\n34567\n", 0, 10, 5, new string[] { "0", "34567", "" }, 10, null)]
        // * line at start, then line ending at end of buffer + 1 (swaps buffer, TRUNCATES), then line ending at end of buffer
        [TestCase("0123\n67\n", 0, 10, 5, new string[] { "0123\r", "", "67" }, 10, null)]
        // * line at start, then line ending at end of buffer + 2 (swaps buffer, TRUNCATES. consumes entire orphaned CRLF), then line ending at end of buffer
        [TestCase("01234\n7\n", 0, 10, 5, new string[] { "01234", "", "7" }, 10, null)]

        // * line at start of buffer, ends without newline at end
        [TestCase("01234", 0, 5, 5, new string[] { "01234" }, 5, null)]
        // * line not at start of buffer, ends without newline at end
        [TestCase("01234", 1, 5, 5, new string[] { "1234" }, 5, null)]
        // * line not at start of buffer, spans buffer, ends without newline at end
        [TestCase("01234567", 3, 8, 5, new string[] { "34567" }, 8, null)]
        [Test]
        public static void TestReadLine(string sDebugStream, long ibStart, long ibLim, long lcbSwapBuffer, string[] rgsExpected, long ibSeekExpected, string sExpectedException)
        {
            TestReadLineWithStreamEndingAtFileLimit(sDebugStream, ibStart, ibLim, lcbSwapBuffer, rgsExpected, ibSeekExpected, sExpectedException);
            TestReadLineWithStreamEndingBeforeFileLimit(sDebugStream, ibStart, ibLim, lcbSwapBuffer, rgsExpected, ibSeekExpected, sExpectedException);
        }

        /*----------------------------------------------------------------------------
        	%%Function: TestReadLineWithStreamEndingAtFileLimit
        	%%Qualified: TCore.StreamEx.BufferedStreamEx.TestReadLineWithStreamEndingAtFileLimit
        	%%Contact: rlittle
        	
        ----------------------------------------------------------------------------*/
        public static void TestReadLineWithStreamEndingAtFileLimit(string sDebugStream, long ibStart, long ibLim, long lcbSwapBuffer, string[] rgsExpected, long ibSeekExpected, string sExpectedException)
        {
            DebugStream stm = DebugStream.StmCreateFromString(sDebugStream);
            BufferedStreamEx stmx = new BufferedStreamEx(stm, ibStart, ibLim, lcbSwapBuffer);
            TestDelegate t = () =>
            {
                foreach (string s in rgsExpected)
                    Assert.AreEqual(s, stmx.ReadLine());

                Assert.AreEqual(ibSeekExpected, stmx.Position());
            };

            if (sExpectedException != null)
                RunTestExpectingException(t, sExpectedException);
            else
                t();
        }

        /*----------------------------------------------------------------------------
        	%%Function: TestReadLineWithStreamEndingBeforeFileLimit
        	%%Qualified: TCore.StreamEx.BufferedStreamEx.TestReadLineWithStreamEndingBeforeFileLimit
        	%%Contact: rlittle
        	
            Add characters to the end of the physical file that should not be 
            visible to the buffer.
        ----------------------------------------------------------------------------*/
        public static void TestReadLineWithStreamEndingBeforeFileLimit(string sDebugStream, long ibStart, long ibLim, long lcbSwapBuffer, string[] rgsExpected, long ibSeekExpected, string sExpectedException)
        {
            DebugStream stm = DebugStream.StmCreateFromString(sDebugStream + "aaaa\n");
            BufferedStreamEx stmx = new BufferedStreamEx(stm, ibStart, ibLim, lcbSwapBuffer);
            TestDelegate t = () =>
            {
                foreach (string s in rgsExpected)
                    Assert.AreEqual(s, stmx.ReadLine());

                Assert.AreEqual(null, stmx.ReadLine());
                Assert.AreEqual(ibSeekExpected, stmx.Position());
            };

            if (sExpectedException != null)
                RunTestExpectingException(t, sExpectedException);
            else
                t();
        }

        // This test doesn't test boundary spanning exhaustively, swapping, etc -- this is all testing with readline.
        // this just tests some basic token identification and parsing to ensure that the underlying
        // buffer exposure is operable without readline. (readline should really just be a fancy token parser)

        // the token syntax is &#NNN;
        [TestCase("&#123;", 0, 6, 6, new string[] { "&#123;" }, new string[] {"", null, ""}, 6, null)]
        [TestCase("01&#123;", 0, 8, 8, new string[] { "&#123;" }, new string[] { "01", null, "" }, 8, null)]
        [TestCase("01&#123;89", 0, 10, 10, new string[] { "&#123;" }, new string[] { "01", null, "89" }, 10, null)]
        [TestCase("01&#456789", 0, 10, 10, new string[] { }, new string[] { "01&#456789" }, 10, null)]
        [TestCase("01&#4a678a", 0, 10, 10, new string[] { }, new string[] { "01&#4a678a" }, 10, null)]
        [TestCase("01&#aa678a", 0, 10, 10, new string[] { }, new string[] { "01&#aa678a" }, 10, null)]
        [TestCase("01&aaa678a", 0, 10, 10, new string[] { }, new string[] { "01&aaa678a" }, 10, null)]
        [TestCase("&#123a&#123;", 0, 12, 12, new string[] { "&#123;" }, new string[] { "&#123a", null, "" }, 12, null)]
        [Test]
        public void TestReadNCR(string sDebugStream, long ibStart, long ibLim, long lcbSwapBuffer,
            string[] rgsNCRExpected, string[] rgsNonNCRExpected, long ibSeekExpected, string sExpectedException)
        {
            DebugStream stm = DebugStream.StmCreateFromString(sDebugStream + "aaaa\n");
            BufferedStreamEx stmx = new BufferedStreamEx(stm, ibStart, ibLim, lcbSwapBuffer);

            TestDelegate t = () =>
            {
                // to do this test, we will construct the string before and after NCRs. a null
                // in the expected string is where an NCR is expected.
                StringBuilder sb = new StringBuilder();
                byte b;
                int iNonNCR = 0;
                int iNCR = 0;
                bool fSkipNextNCRStart = false; // this means we have tried this &, and its not an NCR

                while (stmx.ReadByte(out b) != SwapBuffer.ReadByteBufferState.SourceDataExhausted)
                {
                    if (fSkipNextNCRStart || b != '&')
                    {
                        sb.Append((char) b);
                        fSkipNextNCRStart = false;
                        continue;
                    }

                    // read the possible NCR
                    string sAcceptedNCR;

                    if (!stmx.ReadNCR(out sAcceptedNCR))
                    {
                        fSkipNextNCRStart = true;
                        continue;
                    }

                    // we have an NCR!
                    // confirm we match the before NCR, the null placeholder for the NCR, and the NCR
                    
                    Assert.AreEqual(rgsNonNCRExpected[iNonNCR++], sb.ToString());
                    Assert.AreEqual(rgsNonNCRExpected[iNonNCR++], null);   // there should be a null
                    Assert.AreEqual(rgsNCRExpected[iNCR++], sAcceptedNCR);
                    sb.Clear();
                    // and continue
                }

                // at this point there can't be any NCR's
                Assert.AreEqual(rgsNCRExpected.Length, iNCR);
                Assert.AreEqual(rgsNonNCRExpected[iNonNCR], sb.ToString());

                Assert.AreEqual(ibSeekExpected, stmx.Position());
            };

            if (sExpectedException != null)
                RunTestExpectingException(t, sExpectedException);
            else
                t();
        }
    }

}