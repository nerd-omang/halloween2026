using System.Collections.Generic;
using UnityEngine;

public class VisitorQueue : MonoBehaviour
{
    [SerializeField] Visitor kunmungchiPrefab;
    [SerializeField] Visitor omangiPrefab;
    [Header("Special guests (index = SpecialGuest id: Moku, Myeong7)")]
    [SerializeField] Visitor[] specialPrefabs = new Visitor[SpecialGuest.Count];
    [SerializeField] int[] specialStartScores = { 20, 40 };
    [SerializeField] float specialChance = 0.04f;
    [SerializeField] int disguiseStartScore = 6;
    [SerializeField] float disguiseChanceStart = 0.2f;
    [SerializeField] float disguiseChanceMax = 0.5f;
    [SerializeField] int scoreForMaxDisguise = 200; // every disguise element is fully open from here
    [SerializeField] int firstDisguisedOmangiScore = 8;
    [Header("Disguise ramp: single prop first, paint later")]
    [SerializeField] int multiDisguiseStartScore = 40; // below it: light disguises only, two elements at most
    [SerializeField] int furChangeStartScore = 40;
    [SerializeField] int paintBucketStartScore = 60;
    [SerializeField] int heavyDyeStartScore = 100; // the 60% and 90% dye levels only start to show up here
    [SerializeField] int heavyDyeFullScore = 200; // and are the usual ones from here
    [SerializeField] int lightPairStartScore = 20;   // from here a light disguise may have two elements instead of one
    [SerializeField] float lightPairChance = 0.35f;
    [SerializeField] int sheetStartScore = 60;
    [SerializeField] float sheetChanceStart = 0.15f;
    [SerializeField] float sheetChanceMax = 0.35f;
    [SerializeField] int scoreForMaxSheet = 200;
    [Header("Halloween costumes (visual noise, never the answer)")]
    [SerializeField] int costumeStartScore = 4;
    [SerializeField] float costumeChanceStart = 0.3f;
    [SerializeField] float costumeChanceMax = 0.72f;
    [SerializeField] int scoreForMaxCostume = 30;
    [SerializeField] int maxCostumes = 4;
    [Header("Plain visitors: now and then one comes with nothing at all (no disguise, costume or sheet)")]
    [SerializeField, Range(0f, 0.3f)] float plainChance = 0.06f;
    [Header("Variety: a new look is not allowed to be too close to one of the last few")]
    [SerializeField] int varietyHistory = 3;
    [SerializeField] int varietyTries = 4;
    [SerializeField, Range(0.2f, 1f)] float varietyMaxOverlap = 0.6f; // shared items / all items of the two looks
    [SerializeField] int visibleCount = 8;
    [SerializeField] float frontZ = 2.6f;
    [SerializeField] float spacing = 1.1f;
    [SerializeField] float lateralStep = 0.7f;
    [SerializeField] float groundY = 0.45f;
    [SerializeField] float omangiChanceStart = 0.25f;
    [SerializeField] float omangiChanceMax = 0.55f;
    [SerializeField] int scoreForMaxChance = 40;

    readonly List<Visitor> line = new List<Visitor>();
    readonly List<uint> recentLooks = new List<uint>(); // the item bits of the last few dressed visitors
    bool firstDisguisedOmangiSpawned;
    int spawnedThisGame; // the first two visitors are fixed: a plain Kunmungchi, then a plain Omangi
    readonly bool[] specialSpawned = new bool[SpecialGuest.Count]; // each shows up at most once per game

    public Visitor Front => line.Count > 0 ? line[0] : null;
    public bool FrontReady => Front != null && Front.Arrived && !Front.IsDown;

    Vector3 SlotPos(int index)
    {
        float x = -index * lateralStep;
        return new Vector3(x, groundY, frontZ + index * spacing);
    }

    public void Begin()
    {
        foreach (var v in line)
        {
            if (v != null) Destroy(v.gameObject);
        }
        line.Clear();
        recentLooks.Clear();
        firstDisguisedOmangiSpawned = false;
        spawnedThisGame = 0;
        for (int i = 0; i < specialSpawned.Length; i++) specialSpawned[i] = false;
        while (line.Count < visibleCount)
        {
            Spawn(0, true);
        }
    }

    Visitor Spawn(int score, bool immediate)
    {
        // A visitor spawned at the back reaches the front after the ones ahead are resolved,
        // so judge difficulty by the score the player will have by then.
        int futureScore = score + line.Count;

        var special = TrySpawnSpecial(futureScore, immediate);
        if (special != null) return special;

        float t = Mathf.Clamp01(futureScore / (float)scoreForMaxChance);
        float chance = Mathf.Lerp(omangiChanceStart, omangiChanceMax, t);
        bool isOmangi = Random.value < chance;

        float disguiseT = Mathf.InverseLerp(disguiseStartScore, scoreForMaxDisguise, futureScore);
        float disguiseChance = Mathf.Lerp(disguiseChanceStart, disguiseChanceMax, disguiseT);
        bool disguised = futureScore >= disguiseStartScore && Random.value < disguiseChance;

        if (!firstDisguisedOmangiSpawned && futureScore >= firstDisguisedOmangiScore)
        {
            isOmangi = true;
            disguised = true;
        }
        bool introducesDisguise = isOmangi && disguised && !firstDisguisedOmangiSpawned; // the first disguised Omangi stays disguised
        if (isOmangi && disguised) firstDisguisedOmangiSpawned = true;

        // A fixed, plain start teaches the two answers: candy for the gray one, away for the red one.
        bool teaching = spawnedThisGame < 2;
        if (teaching)
        {
            isOmangi = spawnedThisGame == 1;
            disguised = false;
        }
        spawnedThisGame++;
        // Some visitors are plain whatever the score is: neither a disguise, a costume nor a sheet.
        bool plain = !teaching && !introducesDisguise && Random.value < plainChance;
        if (plain) disguised = false;

        Visitor prefab = isOmangi ? omangiPrefab : kunmungchiPrefab;
        int index = line.Count;
        float costumeT = Mathf.InverseLerp(costumeStartScore, scoreForMaxCostume, futureScore);
        float sheetT = Mathf.InverseLerp(sheetStartScore, scoreForMaxSheet, futureScore);

        // Dress the visitor; when the look is too close to one of the last few, dress a new one instead
        // (a few tries), so the line does not repeat itself.
        Visitor v = null;
        for (int attempt = 0; attempt < varietyTries; attempt++)
        {
            if (v != null) Destroy(v.gameObject);
            v = Instantiate(prefab, SlotPos(visibleCount), Quaternion.identity, transform);
            if (disguised) WearDisguise(v, isOmangi ? VisitorKind.Omangi : VisitorKind.Kunmungchi, futureScore);

            // Costumes are independent of the disguise: more of them, and more often, as the score rises.
            float costumeChance = Mathf.Lerp(costumeChanceStart, costumeChanceMax, costumeT);
            if (!teaching && !plain && futureScore >= costumeStartScore && Random.value < costumeChance && v.TryGetComponent(out VisitorCostume costume))
            {
                int count = Mathf.Clamp(Mathf.RoundToInt(1f + costumeT * (maxCostumes - 1) + Random.Range(-0.6f, 0.6f)), 1, maxCostumes);
                costume.Randomize(count);
            }

            float sheetChance = Mathf.Lerp(sheetChanceStart, sheetChanceMax, sheetT);
            if (!teaching && !plain && futureScore >= sheetStartScore && Random.value < sheetChance && v.TryGetComponent(out GhostSheet sheet))
            {
                sheet.Wear();
            }

            if (teaching || !TooSimilar(v.CurrentOutfit.items)) break;
        }
        Remember(v.CurrentOutfit.items);

        if (immediate) v.Place(SlotPos(index));
        else v.SetTarget(SlotPos(index));
        line.Add(v);
        return v;
    }

    // A plain visitor (no item at all) is always fine; otherwise the share of items two looks have in common must stay low.
    bool TooSimilar(uint items)
    {
        if (items == 0) return false;
        foreach (var other in recentLooks)
        {
            if (other == 0) continue;
            int union = CountBits(items | other);
            if (union > 0 && CountBits(items & other) / (float)union >= varietyMaxOverlap) return true;
        }
        return false;
    }

    void Remember(uint items)
    {
        recentLooks.Add(items);
        while (recentLooks.Count > varietyHistory) recentLooks.RemoveAt(0);
    }

    static int CountBits(uint value)
    {
        int count = 0;
        for (; value != 0; value &= value - 1) count++;
        return count;
    }

    // Now and then a special guest takes the place of an ordinary visitor (never inside the first screenful).
    Visitor TrySpawnSpecial(int futureScore, bool immediate)
    {
        if (immediate || Random.value >= specialChance) return null;
        for (int id = 0; id < SpecialGuest.Count; id++)
        {
            if (specialSpawned[id] || id >= specialPrefabs.Length || specialPrefabs[id] == null) continue;
            if (futureScore < specialStartScores[Mathf.Min(id, specialStartScores.Length - 1)]) continue;

            specialSpawned[id] = true;
            var v = Instantiate(specialPrefabs[id], SlotPos(visibleCount), Quaternion.identity, transform);
            v.gameObject.GetComponent<SpecialGuest>().Id = id;
            int index = line.Count;
            v.SetTarget(SlotPos(index));
            line.Add(v);
            return v;
        }
        return null;
    }

    // Puts a disguise on the visitor. The early game is gentle: exactly one fake prop (no fur change, no
    // paint bucket). Two or more elements come from multiDisguiseStartScore, the fur change (paint) from
    // furChangeStartScore and the paint bucket from paintBucketStartScore.
    void WearDisguise(Visitor v, VisitorKind kind, int futureScore)
    {
        var candidates = new List<ItemDef>();
        foreach (var def in ItemTable.For(kind, ItemCategory.Prop))
        {
            if (def.BringsFur && futureScore < paintBucketStartScore) continue;
            if (futureScore < def.StartScore) continue;
            candidates.Add(def);
        }
        if (futureScore >= furChangeStartScore) candidates.AddRange(ItemTable.For(kind, ItemCategory.Fur));
        if (candidates.Count == 0) return;

        int count = 1;
        if (futureScore < multiDisguiseStartScore)
        {
            // Light disguises: one fake prop, now and then two.
            if (futureScore >= lightPairStartScore && Random.value < lightPairChance) count = 2;
            count = Mathf.Min(count, candidates.Count);
        }
        else
        {
            float skill = Mathf.InverseLerp(multiDisguiseStartScore, scoreForMaxDisguise, futureScore);
            count = DisguiseElements(skill, candidates.Count);
        }

        var outfit = new Outfit { kind = kind };
        for (int picked = 0; picked < count; picked++)
        {
            int i = Random.Range(0, candidates.Count);
            outfit.Set(candidates[i].Bit, true);
            candidates.RemoveAt(i);
        }

        // How thorough the paint job is: sloppy (partial) when the fur change first appears, full later.
        if (v.TryGetComponent(out VisitorDisguise disguise))
        {
            // The light levels (15%, 30%) come in as the fur change does; the heavy ones (60%, 90%) much later.
            int heavyFrom = Mathf.Min(2, disguise.DyeLevelCount - 1);
            float furSkill = Mathf.InverseLerp(furChangeStartScore, scoreForMaxDisguise, futureScore);
            float heavySkill = Mathf.InverseLerp(heavyDyeStartScore, heavyDyeFullScore, futureScore);
            float expected = furSkill * (heavyFrom - 1) + heavySkill * (disguise.DyeLevelCount - heavyFrom);
            outfit.dyeLevel = Mathf.Clamp(Mathf.RoundToInt(expected + Random.Range(-0.6f, 0.6f)), 0, disguise.DyeLevelCount - 1);
        }
        outfit.items = ItemTable.Normalize(kind, outfit.items);
        OutfitApplier.Apply(v, outfit);
    }

    // How many disguise elements to wear once several are allowed: one at first, nearly all by the end of the ramp.
    static int DisguiseElements(float skill, int max)
    {
        float expected = 1f + skill * (max - 1);
        return Mathf.Clamp(Mathf.RoundToInt(expected + Random.Range(-0.6f, 0.6f)), 1, max);
    }

    public void ResolveFront(Vector3 exitDirection, int score)
    {
        if (line.Count == 0) return;
        var leaving = line[0];
        line.RemoveAt(0);
        leaving.Leave(leaving.transform.position + exitDirection * 6f);
        while (line.Count < visibleCount)
        {
            Spawn(score, false);
        }
        for (int i = 0; i < line.Count; i++)
        {
            line[i].SetTarget(SlotPos(i));
        }
    }
}
