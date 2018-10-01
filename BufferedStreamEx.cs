﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TCore.StreamEx
{
    public class PinnedTokenExceedsBufferLength : Exception
    {
        public PinnedTokenExceedsBufferLength() { }
        public PinnedTokenExceedsBufferLength(string msg) : base(msg) { }
        public PinnedTokenExceedsBufferLength(string msg, Exception inner) : base(msg, inner) { }
    }

    public class SourceDataExhausted : Exception
    {
        public SourceDataExhausted() { }
        public SourceDataExhausted(string msg) : base(msg) { }
        public SourceDataExhausted(string msg, Exception inner) : base(msg, inner) { }
    }

    public class BufferedStreamEx
    {
        private Stream m_stm;
        private long m_ibFileStart;
        private long m_ibFileLim;
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
        public BufferedStreamEx(string sFilename, long ibFileStart, long ibFileLim)
        {
            FileStream m_fs = new FileStream(sFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            m_stm = m_fs;

            m_ibFileStart = ibFileStart;
            m_ibFileLim = ibFileLim;
            m_stm.Seek(m_ibFileStart, SeekOrigin.Begin);
            m_bufferMain = new SwapBuffer(m_stm, ibFileLim);
            m_bufferSwap = new SwapBuffer(m_stm, ibFileLim);
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
            %%Function: SwapCurrentBuffer
            %%Qualified: TCore.ListenAz.Stage2.BufferedStreamEx.SwapCurrentBuffer
            %%Contact: rlittle

            swap the current buffer with the other buffer (and fill the new buffer)
            make sure we copy from pinned token start to the end of the buffer to ensure we 
            have a contiguous token

            properly propagates (and resets) the token start offsets

            the current seek position is also set properly to reflect the swap
        ----------------------------------------------------------------------------*/
        bool SwapCurrentBuffer()
        {
            int ibDest = 0;

            if (BufferCurrent.TokenStart != -1)
            {
                // they are requesting that some portion of the current buffer
                // be moved into the swap buffer when we read it -- this way
                // we can have a token span a buffer boundary (though not be
                // larger than a single buffer)
                int ibCopy = BufferCurrent.TokenStart;
                while (ibCopy < BufferCurrent.Lim)
                    BufferOther.Bytes[ibDest++] = BufferCurrent.Bytes[ibCopy++];

                // the current position will be set properly below (in FillBuffer)
            }

            if (!BufferOther.FillBuffer(ibDest))
                return false;

            if (BufferCurrent.TokenStart != -1)
            {
                // reset the tokens properly
                BufferCurrent.ResetPinnedToken();
                BufferOther.PinTokenStartAbsolute(0);
            }

            m_fUseSwapBuffer = !m_fUseSwapBuffer;

            return true;
        }

        /*----------------------------------------------------------------------------
        	%%Function: PinTokenStartRelative
        	%%Qualified: TCore.StreamEx.BufferedStreamEx.PinTokenStartRelative
        	%%Contact: rlittle
        	
            Pin the start of the token, using the given relative value.  (pass 0
            if you want to pin the current position in the buffer. or -1 for the
            previous character, etc)
        ----------------------------------------------------------------------------*/
        public void PinTokenStartRelative(int dib)
        {
            BufferCurrent.PinTokenStartRelative(dib);
        }

        /*----------------------------------------------------------------------------
        	%%Function: ReadByte
        	%%Qualified: TCore.StreamEx.BufferedStreamEx.ReadByte
        	%%Contact: rlittle
        	
        ----------------------------------------------------------------------------*/
        SwapBuffer.ReadByteBufferState ReadByte(out byte b)
        {
            SwapBuffer.ReadByteBufferState state = BufferCurrent.Read(out b);

            if (BufferCurrent.NeedsFilled)
            {
                if (BufferCurrent.TokenStart == 0)
                {
                    return StreamEx.SwapBuffer.ReadByteBufferState.PinnedTokenExceedsBufferLength;

                    // hmm, we have the entire buffer to ourselves, but no line ending was
                    // found. just invent a break here
                    // return Encoding.UTF8.GetString(BufferCurrent.Bytes, ibLineStart, ib - ibLineStart);
                    // the next time they call ReadLine, it will fill the next buffer
                }

                if (!SwapCurrentBuffer())
                {
                    // couldn't fill the next buffer, so we are out of space...just return what we have
                    return StreamEx.SwapBuffer.ReadByteBufferState.SourceDataExhausted;
                    // return Encoding.UTF8.GetString(BufferCurrent.Bytes, ibLineStart, ib - ibLineStart);
                }

                // otherwise, we are good to go.
                // the new buffer has all the stuff we already parsed between [ibLineStart and ib)
                // so rebase them all such that ibLineStart is now 0

                if (BufferCurrent.Cur >= BufferCurrent.Lim)
                    throw new Exception("internal state failure");

                return StreamEx.SwapBuffer.ReadByteBufferState.Succeeded;
            }

            return StreamEx.SwapBuffer.ReadByteBufferState.Succeeded;
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
            if (BufferCurrent.Cur >= BufferCurrent.Lim)
            {
                if (!SwapCurrentBuffer()) // nothing to preserve, just fill the buffer
                    return null;
            }

            // start reading the line from the current position in the current buffer
            
            PinTokenStartRelative(0);
            
            bool fLookingForLF = false;

            while (true)
            {
                // byte b = BufferCurrent.Bytes[ib];
                byte b;

                SwapBuffer.ReadByteBufferState state = ReadByte(out b);
                // Read

                if (b == 0x0a)
                {
                    // we're done. If we were looking for it, great. if not, no matter, we're still done...
                    // BufferCurrent.SetPos(ib + 1);
                    // post read, no change (consume the byte)
                    int cbLineEndingAdjust = fLookingForLF ? 1 : 0;

                    // remember we don't want the line ending as part of the string we construct. Since ib hasn't been adjusted
                    // for this character, the only thing we have to worry about is if there was a leading CR
                    return Encoding.UTF8.GetString(BufferCurrent.Bytes, BufferCurrent.TokenStart,
                        BufferCurrent.Cur - BufferCurrent.TokenStart - cbLineEndingAdjust);
                }

                if (fLookingForLF)
                {
                    // was looking for a matching LF, but didn't find. must be a naked LF
                    // push back this character (or rather,just don't eat it)
                    // BufferCurrent.SetPos(ib);
                    BufferCurrent.Unget();
                    // post read, push back this character (seek back by 1)

                    // remember to chop off the LF
                    return Encoding.UTF8.GetString(BufferCurrent.Bytes, BufferCurrent.TokenStart, BufferCurrent.Cur - BufferCurrent.TokenStart - 1);
                }

                if (b == 0x0d)
                {
                    fLookingForLF = true;
                }

                // otherwise, keep going forward
                if (state == StreamEx.SwapBuffer.ReadByteBufferState.PinnedTokenExceedsBufferLength
                    || state == StreamEx.SwapBuffer.ReadByteBufferState.SourceDataExhausted)
                {
                    // hmm, if we got PinnedTokenExceedsBufferLength, then we have the entire buffer
                    // to ourselves, but no line ending was found. just invent a break here

                    // if we got SourceDataExhausted, then we couldn't fill the next buffer, so we are
                    // out of space...just return what we have

                    // in either case , we do the same thing (and if we are beyond the end of the buffer
                    // the next read will fill the buffer for us)
                    return Encoding.UTF8.GetString(BufferCurrent.Bytes, BufferCurrent.TokenStart, BufferCurrent.Cur - BufferCurrent.TokenStart);
                }

#if OLD
                ib++;

                // all this below should be handled in the read.
                if (ib >= BufferCurrent.Lim)
                {
                    if (ibLineStart == 0)
                    {
                        // hmm, we have the entire buffer to ourselves, but no line ending was
                        // found. just invent a break here
                        return Encoding.UTF8.GetString(BufferCurrent.Bytes, ibLineStart, ib - ibLineStart);
                        // the next time they call ReadLine, it will fill the next buffer
                    }

                    if (!SwapCurrentBuffer(ibLineStart))
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
#endif
            
            }
        }

    }
}
