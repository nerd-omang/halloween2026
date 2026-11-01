using System;
using System.Collections.Generic;

// Compact codes for sending a finished game to a server.
//
// Look code (32 bits): bit 0 = kind, bits 1-2 = fur change level, bits 3-31 = item bits (item bit b is
// code bit 3 + b), so 29 items fit and 16 spare ones are free for later. Every look is wrapped in an
// extended Hamming code (39,32): it fixes one flipped bit and notices two.
//
// Result text: [version 4][score 29][has initials 1][initials 3 x 5][look count 2][looks 39 bits each]
// [checksum 16][reserved 8, must be 0], padded to bytes and written as unpadded base64url. With three looks that
// is exactly 192 bits = 24 bytes = 32 characters. The checksum is CRC-16/CCITT-FALSE over the whole message
// with the checksum bits set to 0 (looks as repaired by the Hamming code); it covers the header, which the
// Hamming code does not. The action of a mistake is not sent: giving candy to an Omangi or sending a
// Kunmungchi away are the only ways to lose a heart, so the kind of the look says which it was.
public static class LookCode
{
    public const int Version = 2;
    public const int ScoreBits = 29;
    public const int MaxScore = (1 << ScoreBits) - 1;
    public const int MaxLooks = 3;
    const int ItemShift = 3;
    const int WordBits = 39;
    const int ChecksumBits = 16;
    const int ReservedBits = 8;

    public static uint Pack(Outfit outfit)
    {
        uint code = (uint)outfit.kind & 1u;
        // The level only means something while the fur is changed.
        int dye = outfit.fur ? Math.Min(Math.Max(outfit.dyeLevel, 0), 3) : 0;
        code |= (uint)dye << 1;
        code |= outfit.items << ItemShift;
        return code;
    }

    public static Outfit Unpack(uint code)
    {
        return new Outfit
        {
            kind = (VisitorKind)(code & 1u),
            dyeLevel = (int)((code >> 1) & 3u),
            items = code >> ItemShift,
        };
    }

    // True when the look is one the game can produce: only known items, and the combination rules hold.
    public static bool IsPossible(Outfit outfit)
    {
        uint known = 0;
        foreach (var def in ItemTable.All) known |= ItemTable.Mask(def.Bit);
        if ((outfit.items & ~known) != 0) return false;
        if (!outfit.fur && outfit.dyeLevel != 0) return false;
        // A sheet hides the look under it but is no combination of its own: only the normal rules apply.
        return ItemTable.IsValid(outfit.kind, outfit.items);
    }

    // ---- Hamming (39,32) ----

    static readonly int[] DataPositions = BuildDataPositions();

    static int[] BuildDataPositions()
    {
        var list = new List<int>();
        for (int position = 1; position < WordBits; position++)
        {
            if ((position & (position - 1)) != 0) list.Add(position); // not a power of two
        }
        return list.ToArray();
    }

    static int Parity(ulong value)
    {
        int count = 0;
        for (; value != 0; value &= value - 1) count++;
        return count & 1;
    }

    // Bit 0 of the word is the overall parity, bits 1..38 are the Hamming positions.
    public static ulong Protect(uint data)
    {
        ulong word = 0;
        for (int i = 0; i < DataPositions.Length; i++)
        {
            if (((data >> i) & 1u) != 0) word |= 1UL << DataPositions[i];
        }
        for (int p = 1; p < WordBits; p <<= 1)
        {
            int parity = 0;
            for (int position = 1; position < WordBits; position++)
            {
                if ((position & p) != 0 && ((word >> position) & 1UL) != 0) parity ^= 1;
            }
            if (parity != 0) word |= 1UL << p;
        }
        if (Parity(word >> 1) != 0) word |= 1UL;
        return word;
    }

    // Recovers the 32 data bits. Fixes a single flipped bit (corrected = true); false when two or more bits are wrong.
    public static bool TryRecover(ulong word, out uint data, out bool corrected)
    {
        data = 0;
        corrected = false;
        if (word >> WordBits != 0) return false;

        int syndrome = 0;
        for (int position = 1; position < WordBits; position++)
        {
            if (((word >> position) & 1UL) != 0) syndrome ^= position;
        }
        bool odd = Parity(word) != 0;
        if (odd)
        {
            if (syndrome >= WordBits) return false;
            word ^= 1UL << syndrome; // syndrome 0 = the overall parity bit itself
            corrected = true;
        }
        else if (syndrome != 0)
        {
            return false;
        }

        for (int i = 0; i < DataPositions.Length; i++)
        {
            if (((word >> DataPositions[i]) & 1UL) != 0) data |= 1u << i;
        }
        return true;
    }

    // ---- Result text ----

    // CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF, no reflection, no final xor).
    public static ushort Crc16(byte[] data)
    {
        int crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b << 8;
            for (int i = 0; i < 8; i++) crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
        }
        return (ushort)crc;
    }

    // The message bits with the given (already protected) look words and checksum.
    static BitBuffer Build(ulong score, bool hasInitials, int[] letters, ulong[] words, ushort checksum)
    {
        var bits = new BitBuffer();
        bits.Write(Version, 4);
        bits.Write(score, ScoreBits);
        bits.Write(hasInitials ? 1UL : 0UL, 1);
        for (int i = 0; i < 3; i++) bits.Write((ulong)letters[i], 5);
        bits.Write((ulong)words.Length, 2);
        foreach (ulong word in words) bits.Write(word, WordBits);
        bits.Write(checksum, ChecksumBits);
        bits.Write(0, ReservedBits);
        return bits;
    }

    static string ToText(byte[] bytes)
    {
        string text = Convert.ToBase64String(bytes);
        return text.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Encode(int score, string initials, IList<Outfit> mistakes)
    {
        bool hasInitials = !string.IsNullOrEmpty(initials);
        var letters = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (hasInitials && i < initials.Length)
            {
                char c = char.ToUpperInvariant(initials[i]);
                if (c >= 'A' && c <= 'Z') letters[i] = c - 'A';
            }
        }

        int count = Math.Min(mistakes?.Count ?? 0, MaxLooks);
        var words = new ulong[count];
        for (int i = 0; i < count; i++) words[i] = Protect(Pack(mistakes[i]));

        ulong clamped = (ulong)Math.Min(Math.Max(score, 0), MaxScore);
        ushort checksum = Crc16(Build(clamped, hasInitials, letters, words, 0).ToBytes());
        return ToText(Build(clamped, hasInitials, letters, words, checksum).ToBytes());
    }

    public static bool TryDecode(string text, out int score, out string initials, out Outfit[] mistakes, out string error)
    {
        score = 0;
        initials = null;
        mistakes = null;
        error = null;

        byte[] bytes;
        try
        {
            string padded = text.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
            bytes = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            error = "not base64url";
            return false;
        }

        const int headerBits = 4 + ScoreBits + 1 + 15 + 2;
        var bits = new BitBuffer(bytes);
        if (!bits.Has(headerBits))
        {
            error = "too short";
            return false;
        }
        if (bits.Read(4) != Version)
        {
            error = "unknown version";
            return false;
        }
        ulong rawScore = bits.Read(ScoreBits);

        bool hasInitials = bits.Read(1) != 0;
        var letters = new int[3];
        bool lettersOk = true;
        for (int i = 0; i < 3; i++)
        {
            ulong letter = bits.Read(5);
            if (letter > 25) lettersOk = false;
            letters[i] = (int)letter;
        }
        if (hasInitials && !lettersOk)
        {
            error = "bad initials";
            return false;
        }

        int count = (int)bits.Read(2);
        if (count > MaxLooks || !bits.Has(count * WordBits + ChecksumBits + ReservedBits))
        {
            error = "bad look count";
            return false;
        }
        var words = new ulong[count];
        var found = new Outfit[count];
        for (int i = 0; i < count; i++)
        {
            if (!TryRecover(bits.Read(WordBits), out uint code, out _))
            {
                error = "look " + i + " damaged";
                return false;
            }
            var outfit = Unpack(code);
            if (!IsPossible(outfit))
            {
                error = "look " + i + " impossible";
                return false;
            }
            found[i] = outfit;
            words[i] = Protect(code);
        }

        ushort checksum = (ushort)bits.Read(ChecksumBits);
        if (bits.Read(ReservedBits) != 0)
        {
            error = "reserved bits set";
            return false;
        }
        int usedBits = headerBits + count * WordBits + ChecksumBits + ReservedBits;
        if (bytes.Length != (usedBits + 7) / 8)
        {
            error = "bad length";
            return false;
        }
        int padBits = bytes.Length * 8 - usedBits;
        if (padBits > 0 && (bytes[bytes.Length - 1] & ((1 << padBits) - 1)) != 0)
        {
            error = "bad padding";
            return false;
        }
        if (Crc16(Build(rawScore, hasInitials, letters, words, 0).ToBytes()) != checksum)
        {
            error = "bad checksum";
            return false;
        }

        score = (int)rawScore;
        if (hasInitials) initials = new string(new[] { (char)('A' + letters[0]), (char)('A' + letters[1]), (char)('A' + letters[2]) });
        mistakes = found;
        return true;
    }

    // ---- Texts written by older versions of the game ----

    // Version 1: [version 4][score 14][has initials 1][initials 3 x 5][look count 2][looks 39 bits each], no checksum.
    // Only used to read results saved on a device before version 2; nothing is sent in this format any more.
    static bool TryDecodeV1(string text, out int score, out string initials, out Outfit[] mistakes)
    {
        score = 0;
        initials = null;
        mistakes = null;

        byte[] bytes;
        try
        {
            string padded = text.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
            bytes = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return false;
        }

        var bits = new BitBuffer(bytes);
        if (!bits.Has(4 + 14 + 1 + 15 + 2) || bits.Read(4) != 1) return false;
        score = (int)bits.Read(14);

        bool hasInitials = bits.Read(1) != 0;
        var letters = new char[3];
        bool lettersOk = true;
        for (int i = 0; i < 3; i++)
        {
            ulong letter = bits.Read(5);
            if (letter > 25) lettersOk = false;
            letters[i] = (char)('A' + letter);
        }
        if (hasInitials)
        {
            if (!lettersOk) return false;
            initials = new string(letters);
        }

        int count = (int)bits.Read(2);
        if (count > MaxLooks || !bits.Has(count * WordBits)) return false;
        mistakes = new Outfit[count];
        for (int i = 0; i < count; i++)
        {
            if (!TryRecover(bits.Read(WordBits), out uint code, out _)) return false;
            var outfit = Unpack(code);
            if (!IsPossible(outfit)) return false;
            mistakes[i] = outfit;
        }
        return true;
    }

    // Brings a saved result text up to the current version. A current text is returned as it is; a version 1 text is
    // decoded and written again in the current format (same score, initials and looks). False for anything else.
    public static bool TryUpgrade(string text, out string upgraded)
    {
        upgraded = null;
        if (string.IsNullOrEmpty(text)) return false;
        if (TryDecode(text, out _, out _, out _, out _))
        {
            upgraded = text;
            return true;
        }
        if (TryDecodeV1(text, out int score, out string initials, out Outfit[] mistakes))
        {
            upgraded = Encode(score, initials, mistakes);
            return true;
        }
        return false;
    }

    // Minimal MSB-first bit packer.
    sealed class BitBuffer
    {
        readonly List<byte> bytes = new List<byte>();
        int bitCount;
        int readPosition;

        public BitBuffer() { }

        public BitBuffer(byte[] data)
        {
            bytes.AddRange(data);
            bitCount = data.Length * 8;
        }

        public bool Has(int count) => readPosition + count <= bitCount;

        public void Write(ulong value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                if (bitCount % 8 == 0) bytes.Add(0);
                if (((value >> i) & 1UL) != 0) bytes[bitCount / 8] |= (byte)(0x80 >> (bitCount % 8));
                bitCount++;
            }
        }

        public ulong Read(int count)
        {
            ulong value = 0;
            for (int i = 0; i < count; i++)
            {
                bool bit = (bytes[readPosition / 8] & (0x80 >> (readPosition % 8))) != 0;
                value = (value << 1) | (bit ? 1UL : 0UL);
                readPosition++;
            }
            return value;
        }

        public byte[] ToBytes() => bytes.ToArray();
    }
}
