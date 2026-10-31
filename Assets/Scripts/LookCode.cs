using System;
using System.Collections.Generic;

// Compact codes for sending a finished game to a server.
//
// Look code (32 bits): bit 0 = kind, bits 1-2 = fur change level, bits 3-31 = item bits (item bit b is
// code bit 3 + b), so 29 items fit and 16 spare ones are free for later. Every look is wrapped in an
// extended Hamming code (39,32): it fixes one flipped bit and notices two.
//
// Result text: [version 4][score 14][has initials 1][initials 3 x 5][look count 2][looks 39 bits each],
// padded to bytes and written as unpadded base64url (at most 27 characters). The action of a mistake is not
// sent: giving candy to an Omangi or sending a Kunmungchi away are the only ways to lose a heart, so the
// kind of the look says which it was.
public static class LookCode
{
    public const int Version = 1;
    public const int MaxScore = (1 << 14) - 1;
    public const int MaxLooks = 3;
    const int ItemShift = 3;
    const int WordBits = 39;

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

    public static string Encode(int score, string initials, IList<Outfit> mistakes)
    {
        var bits = new BitBuffer();
        bits.Write(Version, 4);
        bits.Write((ulong)Math.Min(Math.Max(score, 0), MaxScore), 14);

        bool hasInitials = !string.IsNullOrEmpty(initials);
        bits.Write(hasInitials ? 1UL : 0UL, 1);
        for (int i = 0; i < 3; i++)
        {
            ulong letter = 0;
            if (hasInitials && i < initials.Length)
            {
                char c = char.ToUpperInvariant(initials[i]);
                if (c >= 'A' && c <= 'Z') letter = (ulong)(c - 'A');
            }
            bits.Write(letter, 5);
        }

        int count = Math.Min(mistakes?.Count ?? 0, MaxLooks);
        bits.Write((ulong)count, 2);
        for (int i = 0; i < count; i++) bits.Write(Protect(Pack(mistakes[i])), WordBits);

        string text = Convert.ToBase64String(bits.ToBytes());
        return text.TrimEnd('=').Replace('+', '-').Replace('/', '_');
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

        var bits = new BitBuffer(bytes);
        if (!bits.Has(4 + 14 + 1 + 15 + 2))
        {
            error = "too short";
            return false;
        }
        if (bits.Read(4) != Version)
        {
            error = "unknown version";
            return false;
        }
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
            if (!lettersOk)
            {
                error = "bad initials";
                return false;
            }
            initials = new string(letters);
        }

        int count = (int)bits.Read(2);
        if (count > MaxLooks || !bits.Has(count * WordBits))
        {
            error = "bad look count";
            return false;
        }
        mistakes = new Outfit[count];
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
            mistakes[i] = outfit;
        }
        return true;
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
