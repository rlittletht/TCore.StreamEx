

using System;
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


    }

}