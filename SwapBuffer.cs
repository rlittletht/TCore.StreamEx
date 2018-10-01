
using System;
using System.IO;
using System.Text;

namespace TCore.StreamEx
{
    // ============================================================================
    // S W A P   B U F F E R
    //
    // Allows buffering on top of a FileStream
    // ============================================================================
    public class SwapBuffer
    {
        private byte[] m_rgb = new byte[1024];
        private int m_ibBufferCur = -1;
        private int m_ibBufferLim = -1;
        private readonly long m_ibFileLim;
        private readonly Stream m_stm;


        // this is the pinned seek position for tokens -- if we need to switch
        // to a new buffer, we will ensure that this token is moved into the new
        // buffer (to keep our token together).  This means that tokens are restricted
        // to the buffer size
        private int m_ibTokenStart = -1;

        public int TokenStart => m_ibTokenStart;
        public int Cur => m_ibBufferCur;
        public int Lim => m_ibBufferLim;
        public byte[] Bytes => m_rgb;


        /*----------------------------------------------------------------------------
            %%Function: SwapBuffer
            %%Qualified: TCore.ListenAz.Stage2.BufferedStreamEx.SwapBuffer.SwapBuffer
            %%Contact: rlittle

            create a new buffer on top of the given filestream
        ----------------------------------------------------------------------------*/
        public SwapBuffer(Stream stm, long ibFileLim)
        {
            m_stm = stm;
            m_ibFileLim = ibFileLim;
        }

        /*----------------------------------------------------------------------------
            %%Function: FillBuffer
            %%Qualified: TCore.ListenAz.Stage2.BufferedStreamEx.SwapBuffer.FillBuffer
            %%Contact: rlittle

            ibStart is where we should start filling the buffer at (presumable
            everything before ibStart should be untouched by us because its been
            prefilled)
        ----------------------------------------------------------------------------*/
        public bool FillBuffer(int ibStart)
        {
            if (m_stm.Position >= m_ibFileLim)
                return false;

            long cbToRead = Math.Min(1024 - ibStart, m_ibFileLim - m_stm.Position);

            if (cbToRead != (int)cbToRead)
                throw new Exception("read overflow");

            int cbRead = m_stm.Read(m_rgb, ibStart, (int)cbToRead);

            if (cbRead != cbToRead)
                throw new Exception("read failure");

            m_ibBufferCur = ibStart;
            m_ibBufferLim = ibStart + cbRead;

            return true;
        }

        public void Unget()
        {
            if (m_ibBufferCur == 0)
                throw new Exception("cannot unget the start of the buffer");

            m_ibBufferCur--;
        }

        /*----------------------------------------------------------------------------
        	%%Function: PinTokenStartRelative
        	%%Qualified: TCore.StreamEx.SwapBuffer.PinTokenStartRelative
        	%%Contact: rlittle
        	
            record where in this buffer we would like to pin as the start of a token

            if we have to fill a new buffer, this token will prefill the new buffer
            to ensure the token doesn't span a buffer
        ----------------------------------------------------------------------------*/
        public void PinTokenStartRelative(int dib)
        {
            m_ibTokenStart = m_ibBufferCur + dib;
            if (m_ibTokenStart < 0 || m_ibTokenStart >= m_ibBufferLim)
                throw new Exception("pinned token start outside of buffer");
        }

        /*----------------------------------------------------------------------------
        	%%Function: PinTokenStartAbsolute
        	%%Qualified: TCore.StreamEx.SwapBuffer.PinTokenStartAbsolute
        	%%Contact: rlittle
        	
        ----------------------------------------------------------------------------*/
        public void PinTokenStartAbsolute(int ib)
        {
            m_ibTokenStart = ib;
        }

        /*----------------------------------------------------------------------------
        	%%Function: ResetPinnedToken
        	%%Qualified: TCore.StreamEx.SwapBuffer.ResetPinnedToken
        	%%Contact: rlittle
        	
        ----------------------------------------------------------------------------*/
        public void ResetPinnedToken()
        {
            m_ibTokenStart = -1;
        }

        public bool NeedsFilled => m_ibBufferCur >= m_ibBufferLim;
        public bool HasPinnedToken => m_ibTokenStart != -1;
        
        public enum ReadByteBufferState
        {
            Succeeded,
            PinnedTokenExceedsBufferLength,
            SourceDataExhausted
        }

        /*----------------------------------------------------------------------------
        	%%Function: Read
        	%%Qualified: TCore.StreamEx.SwapBuffer.Read
        	%%Contact: rlittle
        	
        ----------------------------------------------------------------------------*/
        public ReadByteBufferState Read(out byte b)
        {
            b = 0;

            if (m_ibBufferCur >= m_ibBufferLim)
                return ReadByteBufferState.SourceDataExhausted;

            b = Bytes[m_ibBufferCur++];
            return ReadByteBufferState.Succeeded;
        }

        /*----------------------------------------------------------------------------
            %%Function: SetPos
            %%Qualified: TCore.ListenAz.Stage2.BufferedStreamEx.SwapBuffer.SetPos
            %%Contact: rlittle

            set the seek position
        ----------------------------------------------------------------------------*/
        public void SetPos(int ib)
        {
            m_ibBufferCur = ib;
        }

        
    }
}