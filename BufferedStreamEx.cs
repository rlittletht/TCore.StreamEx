using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TCore.StreamEx
{
    public class BufferedStreamEx
    {
        private Stream m_stm;
        private long m_ibStart;
        private long m_ibLim;
        private SwapBuffer m_bufferMain;
        private SwapBuffer m_bufferSwap;

        private bool m_fUseSwapBuffer = false;

        private SwapBuffer BufferCurrent => m_fUseSwapBuffer ? m_bufferSwap : m_bufferMain;
        private SwapBuffer BufferOther => !m_fUseSwapBuffer ? m_bufferSwap : m_bufferMain;

        /*----------------------------------------------------------------------------
            %%Function: BufferedStreamEx
            %%Qualified: TCore.SreamFix.BufferedStreamEx
            %%Contact: rlittle
            
            Create a new BufferedStreamEx, noting the starting offset and the offset
            lim we can read to.

            The file is opened Read only, allowing ReadWrite share opening (to allow
            the other thread to open the file read/write)
        ----------------------------------------------------------------------------*/
        public BufferedStreamEx(string sFilename, long ibStart, long ibLim)
        {
            FileStream m_fs = new FileStream(sFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            m_stm = m_fs;

            m_ibStart = ibStart;
            m_ibLim = ibLim;
            m_stm.Seek(m_ibStart, SeekOrigin.Begin);
            m_bufferMain = new SwapBuffer(m_stm, ibLim);
            m_bufferSwap = new SwapBuffer(m_stm, ibLim);
        }

        /*----------------------------------------------------------------------------
            %%Function: Position
            %%Qualified: TCore.ListenAz.Stage2.BufferedStreamEx.Position
            %%Contact: rlittle
            
        ----------------------------------------------------------------------------*/
        public long Position()
        {
            return m_stm.Position;
        }

        /*----------------------------------------------------------------------------
            %%Function: Close
            %%Qualified: TCore.ListenAz.Stage2.BufferedStreamEx.Close
            %%Contact: rlittle
            
        ----------------------------------------------------------------------------*/
        public void Close()
        {
            m_stm.Close();
            m_stm.Dispose();
            m_stm = null;
        }

        /*----------------------------------------------------------------------------
            %%Function: SwapBuffer
            %%Qualified: TCore.ListenAz.Stage2.BufferedStreamEx.SwapBuffer
            %%Contact: rlittle

            swap the current buffer with the other buffer (and fill the new buffer)
            make sure we copy from ibPreserve to the end of the buffer to ensure we 
            have a contiguous token
        ----------------------------------------------------------------------------*/
        bool SwapBuffer(int ibPreserve)
        {
            int ibDest = 0;

            if (ibPreserve != -1)
            {
                // they are requesting that some portion of the current buffer
                // be moved into the swap buffer when we read it -- this way
                // we can have a token span a buffer boundary (though not be
                // larger than a single buffer)
                int ibCopy = ibPreserve;
                while (ibCopy < BufferCurrent.Lim)
                    BufferOther.Bytes[ibDest++] = BufferCurrent.Bytes[ibCopy++];
            }

            if (!BufferOther.FillBuffer(ibDest))
                return false;

            m_fUseSwapBuffer = !m_fUseSwapBuffer;

            return true;
        }

        /*----------------------------------------------------------------------------
            %%Function: ReadLine
            %%Qualified: TCore.ListenAz.Stage2.BufferedStreamEx.ReadLine
            %%Contact: rlittle

            read a line from the BufferedStreamEx. return the string, or null
            if we are out of lines to read.  we will always return a line at
            the end of the buffer, even if its not terminated with a line ending
        ----------------------------------------------------------------------------*/
        public string ReadLine()
        {
            if (BufferCurrent.Start >= BufferCurrent.Lim)
            {
                if (!SwapBuffer(-1)) // nothing to preserve, just fill the buffer
                    return null;
            }

            // start reading the line from the current position in the current buffer
            int ib = BufferCurrent.Start;
            int ibLineStart = ib;
            bool fLookingForLF = false;

            while (true)
            {
                byte b = BufferCurrent.Bytes[ib];

                if (b == 0x0a)
                {
                    // we're done. If we were looking for it, great. if not, no matter, we're still done...
                    BufferCurrent.SetPos(ib + 1);
                    int cbLineEndingAdjust = fLookingForLF ? 1 : 0;

                    // remember we don't want the line ending as part of the string we construct. Since ib hasn't been adjusted
                    // for this character, the only thing we have to worry about is if there was a leading CR
                    return Encoding.UTF8.GetString(BufferCurrent.Bytes, ibLineStart,
                        ib - ibLineStart - cbLineEndingAdjust);
                }

                if (fLookingForLF)
                {
                    // was looking for a matching LF, but didn't find. must be a naked LF
                    // push back this character (or rather,just don't eat it)
                    BufferCurrent.SetPos(ib);

                    // remember to chop off the LF
                    return Encoding.UTF8.GetString(BufferCurrent.Bytes, ibLineStart, ib - ibLineStart - 1);
                }

                if (b == 0x0d)
                {
                    fLookingForLF = true;
                }
                // otherwise, keep going forward

                ib++;
                if (ib >= BufferCurrent.Lim)
                {
                    if (ibLineStart == 0)
                    {
                        // hmm, we have the entire buffer to ourselves, but no line ending was
                        // found. just invent a break here
                        return Encoding.UTF8.GetString(BufferCurrent.Bytes, ibLineStart, ib - ibLineStart);
                        // the next time they call ReadLine, it will fill the next buffer
                    }

                    if (!SwapBuffer(ibLineStart))
                    {
                        // couldn't fill the next buffer, so we are out of space...just return what we have
                        return Encoding.UTF8.GetString(BufferCurrent.Bytes, ibLineStart, ib - ibLineStart);
                    }

                    // otherwise, we are good to go.
                    // the new buffer has all the stuff we already parsed between [ibLineStart and ib)
                    // so rebase them all such that ibLineStart is now 0
                    ib -= ibLineStart;
                    ibLineStart = 0;

                    if (ib >= BufferCurrent.Lim)
                        throw new Exception("internal state failure");
                }
            }
        }

    }
}
