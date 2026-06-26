namespace ServerRemote.App.Services.H264;

/// <summary>
/// Helper functions for Annex-B H.264 bitstreams: NAL iteration, parameter-set extraction
/// (SPS/PPS for the <c>MediaCodec</c> csd) and resolution detection from the SPS.
/// </summary>
internal static class H264Utils
{
    /// <summary>
    /// Iterates over the Annex-B NAL units. Returns <c>(nalStart, end)</c> per unit, where
    /// <c>nalStart</c> points to the NAL header byte (directly after the start code) and
    /// <c>end</c> is exclusive, just before the next start code (or the end of the buffer).
    /// </summary>
    public static IEnumerable<(int nalStart, int end)> EnumerateNals(byte[] d)
    {
        int n = d.Length;
        int i = 0;
        int payload = -1;

        while (i + 2 < n)
        {
            int scLen = 0;
            if (i + 3 < n && d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 0 && d[i + 3] == 1)
                scLen = 4;
            else if (d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 1)
                scLen = 3;

            if (scLen > 0)
            {
                if (payload >= 0)
                    yield return (payload, i);
                payload = i + scLen;
                i += scLen;
            }
            else
            {
                i++;
            }
        }

        if (payload >= 0 && payload < n)
            yield return (payload, n);
    }

    public static int NalType(byte[] d, int nalStart) => d[nalStart] & 0x1F;

    /// <summary>Does the frame contain at least one SPS (NAL type 7)? (= a real parameter-set carrier).</summary>
    public static bool ContainsSps(byte[] frame)
    {
        foreach (var (nalStart, _) in EnumerateNals(frame))
            if (NalType(frame, nalStart) == 7)
                return true;
        return false;
    }

    /// <summary>
    /// Extracts SPS (type 7) and PPS (type 8) as Annex-B blocks (including the 4-byte start code) —
    /// exactly the format that <c>MediaFormat</c> expects as <c>csd-0</c>/<c>csd-1</c>.
    /// </summary>
    public static bool TryGetParameterSets(byte[] frame, out byte[]? sps, out byte[]? pps)
    {
        sps = pps = null;
        foreach (var (nalStart, end) in EnumerateNals(frame))
        {
            int t = NalType(frame, nalStart);
            if (t == 7 && sps is null)
                sps = WithStartCode(frame, nalStart, end);
            else if (t == 8 && pps is null)
                pps = WithStartCode(frame, nalStart, end);
        }
        return sps is not null && pps is not null;
    }

    private static byte[] WithStartCode(byte[] d, int nalStart, int end)
    {
        int len = end - nalStart;
        var buf = new byte[4 + len];
        buf[3] = 1; // 00 00 00 01
        Array.Copy(d, nalStart, buf, 4, len);
        return buf;
    }

    /// <summary>Reads width/height from the frame's first SPS (in pixels, accounting for crop).</summary>
    public static bool TryGetDimensions(byte[] frame, out int width, out int height)
    {
        width = height = 0;
        foreach (var (nalStart, end) in EnumerateNals(frame))
        {
            if (NalType(frame, nalStart) != 7)
                continue;

            // RBSP without the NAL header byte and without emulation-prevention bytes (00 00 03 -> 00 00).
            var rbsp = Unescape(frame, nalStart + 1, end);
            return ParseSps(rbsp, out width, out height);
        }
        return false;
    }

    private static byte[] Unescape(byte[] d, int start, int end)
    {
        var outBuf = new List<byte>(end - start);
        int zeros = 0;
        for (int i = start; i < end; i++)
        {
            byte b = d[i];
            if (zeros >= 2 && b == 0x03)
            {
                // Discard emulation-prevention byte.
                zeros = 0;
                continue;
            }
            outBuf.Add(b);
            zeros = b == 0 ? zeros + 1 : 0;
        }
        return outBuf.ToArray();
    }

    // Minimal Exp-Golomb SPS parser — only as far as needed for the resolution.
    private static bool ParseSps(byte[] rbsp, out int width, out int height)
    {
        width = height = 0;
        var r = new BitReader(rbsp);
        try
        {
            int profileIdc = r.ReadBits(8);
            r.ReadBits(8);  // constraint flags + reserved
            r.ReadBits(8);  // level_idc
            r.ReadUe();     // seq_parameter_set_id

            int chromaFormatIdc = 1; // default 4:2:0
            if (profileIdc is 100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135)
            {
                chromaFormatIdc = r.ReadUe();
                if (chromaFormatIdc == 3)
                    r.ReadBits(1); // separate_colour_plane_flag
                r.ReadUe();        // bit_depth_luma_minus8
                r.ReadUe();        // bit_depth_chroma_minus8
                r.ReadBits(1);     // qpprime_y_zero_transform_bypass_flag
                if (r.ReadBits(1) == 1) // seq_scaling_matrix_present_flag
                {
                    int lists = chromaFormatIdc != 3 ? 8 : 12;
                    for (int i = 0; i < lists; i++)
                        if (r.ReadBits(1) == 1) // scaling_list_present_flag
                            SkipScalingList(r, i < 6 ? 16 : 64);
                }
            }

            r.ReadUe(); // log2_max_frame_num_minus4
            int pocType = r.ReadUe();
            if (pocType == 0)
            {
                r.ReadUe(); // log2_max_pic_order_cnt_lsb_minus4
            }
            else if (pocType == 1)
            {
                r.ReadBits(1); // delta_pic_order_always_zero_flag
                r.ReadSe();    // offset_for_non_ref_pic
                r.ReadSe();    // offset_for_top_to_bottom_field
                int n = r.ReadUe();
                for (int i = 0; i < n; i++)
                    r.ReadSe();
            }

            r.ReadUe();    // max_num_ref_frames
            r.ReadBits(1); // gaps_in_frame_num_value_allowed_flag

            int picWidthInMbsMinus1 = r.ReadUe();
            int picHeightInMapUnitsMinus1 = r.ReadUe();
            int frameMbsOnlyFlag = r.ReadBits(1);
            if (frameMbsOnlyFlag == 0)
                r.ReadBits(1); // mb_adaptive_frame_field_flag
            r.ReadBits(1);     // direct_8x8_inference_flag

            int cropLeft = 0, cropRight = 0, cropTop = 0, cropBottom = 0;
            if (r.ReadBits(1) == 1) // frame_cropping_flag
            {
                cropLeft = r.ReadUe();
                cropRight = r.ReadUe();
                cropTop = r.ReadUe();
                cropBottom = r.ReadUe();
            }

            int subWidthC = chromaFormatIdc is 1 or 2 ? 2 : 1;
            int subHeightC = chromaFormatIdc == 1 ? 2 : 1;
            int cropUnitX = chromaFormatIdc == 0 ? 1 : subWidthC;
            int cropUnitY = (chromaFormatIdc == 0 ? 1 : subHeightC) * (2 - frameMbsOnlyFlag);

            width = (picWidthInMbsMinus1 + 1) * 16 - (cropLeft + cropRight) * cropUnitX;
            height = (2 - frameMbsOnlyFlag) * (picHeightInMapUnitsMinus1 + 1) * 16
                     - (cropTop + cropBottom) * cropUnitY;

            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SkipScalingList(BitReader r, int size)
    {
        int lastScale = 8, nextScale = 8;
        for (int j = 0; j < size; j++)
        {
            if (nextScale != 0)
            {
                int delta = r.ReadSe();
                nextScale = (lastScale + delta + 256) % 256;
            }
            lastScale = nextScale == 0 ? lastScale : nextScale;
        }
    }

    // Big-endian bit reader with Exp-Golomb support over an RBSP.
    private sealed class BitReader
    {
        private readonly byte[] _d;
        private int _bit;

        public BitReader(byte[] d) => _d = d;

        public int ReadBits(int count)
        {
            int v = 0;
            for (int i = 0; i < count; i++)
            {
                int byteIndex = _bit >> 3;
                if (byteIndex >= _d.Length)
                    throw new EndOfStreamException();
                int shift = 7 - (_bit & 7);
                v = (v << 1) | ((_d[byteIndex] >> shift) & 1);
                _bit++;
            }
            return v;
        }

        public int ReadUe()
        {
            int zeros = 0;
            while (ReadBits(1) == 0)
                zeros++;
            int value = 0;
            if (zeros > 0)
                value = ReadBits(zeros);
            return (1 << zeros) - 1 + value;
        }

        public int ReadSe()
        {
            int k = ReadUe();
            int sign = (k & 1) == 0 ? -1 : 1;
            return sign * ((k + 1) >> 1);
        }
    }
}
