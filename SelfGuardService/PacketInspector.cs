using System.Text;

namespace SelfGuardService;

/// <summary>Parses raw IPv4/IPv6 packets to extract DNS query names and TLS SNI hostnames.</summary>
internal static class PacketInspector
{
    public static string? ExtractHost(byte[] pkt, int len)
    {
        if (len < 20) return null;
        int ver = pkt[0] >> 4;
        int ipHdr; byte proto;
        if (ver == 4) { ipHdr = (pkt[0] & 0x0F) * 4; proto = pkt[9]; }
        else if (ver == 6) { ipHdr = 40; proto = pkt[6]; }
        else return null;
        if (len < ipHdr + 8) return null;

        int dstPort = (pkt[ipHdr + 2] << 8) | pkt[ipHdr + 3];

        if (proto == 17 && dstPort == 53)              // UDP DNS
            return ParseDns(pkt, ipHdr + 8, len);

        if (proto == 6)                                // TCP
        {
            int tcpHdr = ((pkt[ipHdr + 12] >> 4) & 0x0F) * 4;
            int payload = ipHdr + tcpHdr;
            if (payload >= len) return null;
            if (dstPort == 53) return ParseDns(pkt, payload + 2, len);   // DNS over TCP (2-byte length prefix)
            if (dstPort == 443) return ParseSni(pkt, payload, len);
        }
        return null;
    }

    private static string? ParseDns(byte[] p, int off, int len)
    {
        int i = off + 12;                    // skip DNS header
        if (i >= len) return null;
        var sb = new StringBuilder();
        while (i < len)
        {
            int l = p[i++];
            if (l == 0) break;
            if ((l & 0xC0) != 0 || i + l > len) return null;
            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(p, i, l));
            i += l;
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static string? ParseSni(byte[] p, int off, int len)
    {
        // TLS record: 0x16 (handshake), version(2), length(2); Handshake type 0x01 = ClientHello
        if (off + 43 > len || p[off] != 0x16 || p[off + 5] != 0x01) return null;
        int i = off + 5 + 4 + 2 + 32;         // hdr + hs hdr + version + random
        if (i >= len) return null;
        i += 1 + p[i];                         // session id
        if (i + 2 > len) return null;
        i += 2 + ((p[i] << 8) | p[i + 1]);     // cipher suites
        if (i >= len) return null;
        i += 1 + p[i];                         // compression
        if (i + 2 > len) return null;
        int extEnd = i + 2 + ((p[i] << 8) | p[i + 1]);
        i += 2;
        while (i + 4 <= len && i + 4 <= extEnd)
        {
            int type = (p[i] << 8) | p[i + 1];
            int elen = (p[i + 2] << 8) | p[i + 3];
            i += 4;
            if (type == 0x0000 && i + 5 <= len)  // server_name
            {
                int nameLen = (p[i + 3] << 8) | p[i + 4];
                if (i + 5 + nameLen <= len)
                    return Encoding.ASCII.GetString(p, i + 5, nameLen);
                return null;
            }
            i += elen;
        }
        return null;
    }
}
