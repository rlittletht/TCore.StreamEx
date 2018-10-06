using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TCore.StreamEx
{
    public partial class BufferedStreamEx
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
        	%%Function: BufferedStreamEx
        	%%Qualified: TCore.StreamEx.BufferedStreamEx.BufferedStreamEx
        	%%Contact: rlittle
        	
        ----------------------------------------------------------------------------*/
        public BufferedStreamEx(Stream stm, long ibFileStart, long ibFileLim, long cbSwapBuffer = 1024)
        {
            m_stm = stm;
            m_ibFileStart = ibFileStart;
            m_ibFileLim = ibFileLim;
            m_stm.Seek(m_ibFileStart, SeekOrigin.Begin);
            m_bufferMain = new SwapBuffer(m_stm, ibFileLim, cbSwapBuffer);
            m_bufferSwap = new SwapBuffer(m_stm, ibFileLim, cbSwapBuffer);
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
        	%%Function: ResetPinnedToken
        	%%Qualified: TCore.StreamEx.BufferedStreamEx.ResetPinnedToken
        	%%Contact: rlittle
            
            we are no longer pinning a token. often done at the start of reading a 
            line (or while we are looking for the start of a token)
        ----------------------------------------------------------------------------*/
        public void ResetPinnedToken()
        {
            BufferCurrent.ResetPinnedToken();
        }

        /*----------------------------------------------------------------------------
        	%%Function: FillBuffer
        	%%Qualified: TCore.StreamEx.BufferedStreamEx.FillBuffer
        	%%Contact: rlittle
        	
            Fill the current buffer (swap, then fill)
        ----------------------------------------------------------------------------*/
        SwapBuffer.ReadByteBufferState FillBuffer()
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

        /*----------------------------------------------------------------------------
        	%%Function: ReadByte
        	%%Qualified: TCore.StreamEx.BufferedStreamEx.ReadByte
        	%%Contact: rlittle
        	
        ----------------------------------------------------------------------------*/
        public SwapBuffer.ReadByteBufferState ReadByte(out byte b)
        {
            b = 0;
            SwapBuffer.ReadByteBufferState state;

            // if the buffer needs filled, then let's fill it
            if (BufferCurrent.NeedsFilled)
            {
                state = FillBuffer();
                if (state != SwapBuffer.ReadByteBufferState.Succeeded)
                    return state;
            }

            state = BufferCurrent.Read(out b);

            return state;
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
            ResetPinnedToken();

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
                byte b;

                SwapBuffer.ReadByteBufferState state = ReadByte(out b);
                // after read, process the read then check the state...

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

                if (b == 0x0a)
                {
                    // we're done. If we were looking for it, great. if not, no matter, we're still done...
                    int cbLineEndingAdjust = fLookingForLF ? 2 : 1;

                    // remember we don't want the line ending as part of the string we construct. Since ib hasn't been adjusted
                    // for this character, the only thing we have to worry about is if there was a leading CR
                    return Encoding.UTF8.GetString(BufferCurrent.Bytes, BufferCurrent.TokenStart,
                        BufferCurrent.Cur - BufferCurrent.TokenStart - cbLineEndingAdjust);
                }

                if (fLookingForLF)
                {
                    // was looking for a matching LF, but didn't find. must be a naked LF
                    // push back this character (or rather,just don't eat it)
                    BufferCurrent.Unget();

                    // remember to chop off the LF
                    return Encoding.UTF8.GetString(BufferCurrent.Bytes, BufferCurrent.TokenStart, BufferCurrent.Cur - BufferCurrent.TokenStart - 1);
                }

                if (b == 0x0d)
                {
                    fLookingForLF = true;
                }

            }
        }

        private byte[] RgbCopyPinnedToken()
        {
            if (!BufferCurrent.HasPinnedToken)
                throw new Exception("no pinned token to copy");

            byte[] rgb = new byte[BufferCurrent.Cur - BufferCurrent.TokenStart];
            Buffer.BlockCopy(BufferCurrent.Bytes, BufferCurrent.TokenStart, rgb, 0, BufferCurrent.Cur - BufferCurrent.TokenStart);

            return rgb;
        }

        /*----------------------------------------------------------------------------
        	%%Function: ReadNCR
        	%%Qualified: TCore.StreamEx.BufferedStreamEx.ReadNCR
        	%%Contact: rlittle
        	
            assumes we just read a "&"

            because we are pinning the token starting at &, if we determine that this
            isn't an NCR, then we can just reset the read location to the start and
            let the caller continue (we will return false).

            it is the caller's responsibility to NOT try to parse this & again as an
            NCR.
        ----------------------------------------------------------------------------*/
        public bool ReadNCR(out string sNCR)
        {
            PinTokenStartRelative(-1);
            byte b;
            bool fValidNCR = false;

            // look for the #
            SwapBuffer.ReadByteBufferState state = ReadByte(out b);

            if (b == '#')
            {
                bool fNeedSingleDigit = true;

                while (state == SwapBuffer.ReadByteBufferState.Succeeded)
                {
                    state = ReadByte(out b);
                    if (b >= '0' && b <= '9')
                    {
                        fNeedSingleDigit = false;
                        continue;
                    }

                    if (b == ';')
                    {
                        if (fNeedSingleDigit == false)
                            fValidNCR = true;

                        break;
                    }

                    break;  // not a valid NCR
                }
            }

            if (!fValidNCR)
            {
                BufferCurrent.SetPos(BufferCurrent.TokenStart);
                ResetPinnedToken();
                sNCR = null;
                return false;
            }

            // we have a valid NCR. 
            sNCR =  Encoding.UTF8.GetString(BufferCurrent.Bytes, BufferCurrent.TokenStart, BufferCurrent.Cur - BufferCurrent.TokenStart);
            ResetPinnedToken();
            return true;
        }
    }
}
