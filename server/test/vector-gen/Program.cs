using System;
using System.Collections.Generic;
using System.Text;

static class P
{
    static string J(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static byte[] Unb64(string t)
    {
        string p = t.Replace('-', '+').Replace('_', '/');
        p = p.PadRight((p.Length + 3) / 4 * 4, '=');
        return Convert.FromBase64String(p);
    }

    static string Outcome(string text)
    {
        if (LookCode.TryDecode(text, out int score, out string initials, out Outfit[] looks, out string err))
        {
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"score\":" + score + ",\"initials\":" + J(initials) + ",\"looks\":[");
            for (int i = 0; i < looks.Length; i++)
            {
                if (i > 0) sb.Append(',');
                uint code = LookCode.Pack(looks[i]);
                sb.Append("{\"code\":" + code + ",\"kind\":" + (int)looks[i].kind + ",\"fur\":" + looks[i].dyeLevel + ",\"items\":" + looks[i].items + "}");
            }
            sb.Append("]}");
            return sb.ToString();
        }
        return "{\"ok\":false,\"error\":" + J(err) + "}";
    }

    static void Main(string[] args)
    {
        var rng = new Random(20261031);
        var cases = new List<string>();
        void Add(string name, string text) => cases.Add("{\"name\":" + J(name) + ",\"text\":" + J(text) + ",\"expect\":" + Outcome(text) + "}");

        uint known = 0;
        foreach (var d in ItemTable.All) known |= ItemTable.Mask(d.Bit);
        Outfit RandomValid()
        {
            var kind = (VisitorKind)rng.Next(2);
            uint items = (uint)rng.Next() & known;
            items = ItemTable.Normalize(kind, items);
            var o = new Outfit { kind = kind, items = items };
            o.dyeLevel = o.fur ? rng.Next(4) : 0;
            return o;
        }
        string[] inis = { "AAA", "ZZZ", "KIM", "OMG", null, "", "abc" };
        for (int g = 0; g < 40; g++)
        {
            int n = g % 4;
            var looks = new List<Outfit>();
            for (int i = 0; i < n; i++) looks.Add(RandomValid());
            int score = g < 6 ? new[] { 0, 1, 16383, 16384, LookCode.MaxScore, 9999 }[g] : rng.Next(0, LookCode.MaxScore + 1);
            string t = LookCode.Encode(score, inis[g % inis.Length], looks);
            Add("valid" + g, t);
        }

        int first = 4 + LookCode.ScoreBits + 1 + 15 + 2;
        {
            var looks = new List<Outfit> { RandomValid(), RandomValid(), RandomValid() };
            string t = LookCode.Encode(777, "WIN", looks);
            byte[] raw = Unb64(t);
            int total = raw.Length * 8;
            // every single flipped bit of a full message: looks are repaired, everything else is refused or changes the message
            for (int bit = 0; bit < total; bit++)
            {
                var b = (byte[])raw.Clone();
                b[bit / 8] ^= (byte)(0x80 >> (bit % 8));
                Add("flip1_bit" + bit, B64(b));
            }
            for (int k = 0; k < 200; k++)
            {
                int bit1 = rng.Next(total), bit2 = rng.Next(total);
                if (bit1 == bit2) continue;
                bool inLooks1 = bit1 >= first && bit1 < first + 3 * 39, inLooks2 = bit2 >= first && bit2 < first + 3 * 39;
                var b = (byte[])raw.Clone();
                b[bit1 / 8] ^= (byte)(0x80 >> (bit1 % 8));
                b[bit2 / 8] ^= (byte)(0x80 >> (bit2 % 8));
                Add("flip2_" + bit1 + "_" + bit2 + (inLooks1 && inLooks2 ? "_looks" : ""), B64(b));
            }
        }

        void Forge(string name, Outfit o) => Add(name, LookCode.Encode(10, "ABC", new List<Outfit> { o }));
        Forge("forged_fur_level_without_fur", new Outfit { kind = VisitorKind.Omangi, items = 0, dyeLevel = 2 });
        Forge("forged_horn_on_omangi", new Outfit { kind = VisitorKind.Omangi, items = ItemTable.Mask(1) });
        Forge("forged_cone_nose_on_kun", new Outfit { kind = VisitorKind.Kunmungchi, items = ItemTable.Mask(3) });
        Forge("forged_two_head_props", new Outfit { kind = VisitorKind.Kunmungchi, items = ItemTable.Mask(1) | ItemTable.Mask(2) });
        Forge("forged_bucket_without_fur", new Outfit { kind = VisitorKind.Kunmungchi, items = ItemTable.Mask(6) });
        Forge("forged_hat_under_bucket", new Outfit { kind = VisitorKind.Kunmungchi, items = ItemTable.Mask(6) | ItemTable.Mask(0) | ItemTable.Mask(7) });
        Forge("forged_unknown_item_bit26", new Outfit { kind = VisitorKind.Kunmungchi, items = ItemTable.Mask(26) });
        Forge("forged_maid_dress_with_bunny_suit", new Outfit { kind = VisitorKind.Kunmungchi, items = ItemTable.Mask(25) | ItemTable.Mask(24) });
        Forge("forged_bikini_with_bunny_suit", new Outfit { kind = VisitorKind.Omangi, items = ItemTable.Mask(23) | ItemTable.Mask(24) });
        Forge("forged_star_sunglasses_with_eye_patch", new Outfit { kind = VisitorKind.Kunmungchi, items = ItemTable.Mask(15) | ItemTable.Mask(22) });
        Forge("forged_maid_with_horn_band", new Outfit { kind = VisitorKind.Kunmungchi, items = ItemTable.Mask(1) | ItemTable.Mask(13) });
        Forge("forged_hat_and_bunny", new Outfit { kind = VisitorKind.Omangi, items = ItemTable.Mask(7) | ItemTable.Mask(14) });
        Forge("forged_star_with_x_glasses", new Outfit { kind = VisitorKind.Omangi, items = ItemTable.Mask(5) | ItemTable.Mask(15) });
        Forge("forged_sheet_and_pumpkin", new Outfit { kind = VisitorKind.Omangi, items = ItemTable.Mask(12) | ItemTable.Mask(21) });
        Forge("pumpkin_ok", new Outfit { kind = VisitorKind.Omangi, items = ItemTable.Mask(21) });
        Forge("forged_broom_and_lollipop", new Outfit { kind = VisitorKind.Omangi, items = ItemTable.Mask(18) | ItemTable.Mask(19) });
        Forge("forged_eye_patch_with_x_glasses", new Outfit { kind = VisitorKind.Omangi, items = ItemTable.Mask(5) | ItemTable.Mask(17) });
        Forge("bunny_and_alien_ok", new Outfit { kind = VisitorKind.Omangi, items = ItemTable.Mask(14) | ItemTable.Mask(16) });
        Forge("forged_unknown_item_bit28", new Outfit { kind = VisitorKind.Omangi, items = ItemTable.Mask(28) });
        Forge("forged_sheet_ok", new Outfit { kind = VisitorKind.Omangi, items = ItemTable.Mask(12) });
        Forge("fur_only_level3", new Outfit { kind = VisitorKind.Kunmungchi, items = ItemTable.Mask(0), dyeLevel = 3 });
        Forge("bucket_with_fur_ok", new Outfit { kind = VisitorKind.Kunmungchi, items = ItemTable.Mask(6) | ItemTable.Mask(0), dyeLevel = 1 });

        foreach (var g in new[] { "", "A", "AAAA", "!!!!", "A=A=", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" }) Add("garbage_" + g, g);
        Add("clamped_score", LookCode.Encode(int.MaxValue, "MAX", new List<Outfit>()));
        Add("negative_score", LookCode.Encode(-5, null, new List<Outfit>()));

        var items = new StringBuilder("[");
        for (int i = 0; i < ItemTable.All.Length; i++)
        {
            var d = ItemTable.All[i];
            if (i > 0) items.Append(',');
            items.Append("{\"bit\":" + d.Bit + ",\"id\":" + J(d.Id) + ",\"category\":" + (int)d.Category + ",\"slot\":" + (int)d.Slot +
                ",\"kinds\":" + d.Kinds + ",\"bringsFur\":" + (d.BringsFur ? "true" : "false") + ",\"coversHead\":" + (d.CoversHead ? "true" : "false") +
                ",\"coversEyes\":" + (d.CoversEyes ? "true" : "false") + ",\"headband\":" + (d.Headband ? "true" : "false") +
                ",\"headPriority\":" + d.HeadPriority + "}");
        }
        items.Append(']');

        // every look code the game can produce (kind x fur level x known item bits)
        var validLooks = new List<uint>();
        for (int kind = 0; kind < 2; kind++)
            for (int dye = 0; dye < 4; dye++)
                for (uint it = 0; it < (1u << 26); it++)
                {
                    var o = new Outfit { kind = (VisitorKind)kind, items = it, dyeLevel = dye };
                    if (LookCode.IsPossible(o)) validLooks.Add(LookCode.Pack(o));
                }
        validLooks.Sort();
        long h = 0;
        foreach (var c in validLooks) h = (h * 31 + c) % 1000000007;

        var sbv = new StringBuilder();
        sbv.Append("{\n\"generatedBy\":\"C# LookCode/ItemTable from Assets/Scripts (see server/test/README.md)\",\n");
        sbv.Append("\"itemTable\":" + items + ",\n");
        sbv.Append("\"validLookCount\":" + validLooks.Count + ",\n");
        sbv.Append("\"validLookChecksum\":" + h + ",\n");
        sbv.Append("\"cases\":[\n" + string.Join(",\n", cases) + "\n]\n}\n");
        System.IO.File.WriteAllText(args.Length > 0 ? args[0] : "vectors.json", sbv.ToString());
        Console.WriteLine(cases.Count + " cases, " + validLooks.Count + " valid looks");
    }
}
