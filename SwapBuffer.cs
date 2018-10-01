
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
        private int m_ibBufferStart = -1;
        private int m_ibBufferLim = -1;
        private readonly long m_ibFileLim;
        private readonly Stream m_stm;

        public int Start => m_ibBufferStart;
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

            m_ibBufferStart = ibStart;
            m_ibBufferLim = ibStart + cbRead;

            return true;
        }

        /*----------------------------------------------------------------------------
            %%Function: SetPos
            %%Qualified: TCore.ListenAz.Stage2.BufferedStreamEx.SwapBuffer.SetPos
            %%Contact: rlittle

            set the seek position
        ----------------------------------------------------------------------------*/
        public void SetPos(int ib)
        {
            m_ibBufferStart = ib;
        }
    }
}