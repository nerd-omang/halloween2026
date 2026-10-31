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
    [SerializeField] int disguiseStartScore = 3;
    [SerializeField] float disguiseChanceStart = 0.2f;
    [SerializeField] float disguiseChanceMax = 0.5f;
    [SerializeField] int scoreForMaxDisguise = 30;
    [SerializeField] int firstDisguisedOmangiScore = 5;
    [Header("Disguise ramp: single prop first, paint later")]
    [SerializeField] int multiDisguiseStartScore = 12;
    [SerializeField] int furChangeStartScore = 15;
    [SerializeField] int paintBucketStartScore = 22;
    [SerializeField] int sheetStartScore = 10;
    [SerializeField] float sheetChanceStart = 0.15f;
    [SerializeField] float sheetChanceMax = 0.35f;
    [SerializeField] int scoreForMaxSheet = 35;
    [Header("Halloween costumes (visual noise, never the answer)")]
    [SerializeField] int costumeStartScore = 10;
    [SerializeField] float costumeChanceStart = 0.15f;
    [SerializeField] float costumeChanceMax = 0.6f;
    [SerializeField] int scoreForMaxCostume = 40;
    [SerializeField] int maxCostumes = 3;
    [SerializeField] int visibleCount = 8;
    [SerializeField] float frontZ = 2.6f;
    [SerializeField] float spacing = 1.1f;
    [SerializeField] float lateralStep = 0.7f;
    [SerializeField] float groundY = 0.45f;
    [SerializeField] float omangiChanceStart = 0.25f;
    [SerializeField] float omangiChanceMax = 0.55f;
    [SerializeField] int scoreForMaxChance = 40;

    readonly List<Visitor> line = new List<Visitor>();
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
        if (isOmangi && disguised) firstDisguisedOmangiSpawned = true;

        // A fixed, plain start teaches the two answers: candy for the gray one, away for the red one.
        bool teaching = spawnedThisGame < 2;
        if (teaching)
        {
            isOmangi = spawnedThisGame == 1;
            disguised = false;
        }
        spawnedThisGame++;

        Visitor prefab = isOmangi ? omangiPrefab : kunmungchiPrefab;
        int index = line.Count;
        var v = Instantiate(prefab, SlotPos(visibleCount), Quaternion.identity, transform);
        if (disguised) WearDisguise(v, isOmangi ? VisitorKind.Omangi : VisitorKind.Kunmungchi, futureScore);

        // Costumes are independent of the disguise: more of them, and more often, as the score rises.
        float costumeT = Mathf.InverseLerp(costumeStartScore, scoreForMaxCostume, futureScore);
        float costumeChance = Mathf.Lerp(costumeChanceStart, costumeChanceMax, costumeT);
        if (!teaching && futureScore >= costumeStartScore && Random.value < costumeChance && v.TryGetComponent(out VisitorCostume costume))
        {
            int count = Mathf.Clamp(Mathf.RoundToInt(1f + costumeT * (maxCostumes - 1) + Random.Range(-0.6f, 0.6f)), 1, maxCostumes);
            costume.Randomize(count);
        }

        float sheetT = Mathf.InverseLerp(sheetStartScore, scoreForMaxSheet, futureScore);
        float sheetChance = Mathf.Lerp(sheetChanceStart, sheetChanceMax, sheetT);
        if (!teaching && futureScore >= sheetStartScore && Random.value < sheetChance && v.TryGetComponent(out GhostSheet sheet))
        {
            sheet.Wear();
        }

        if (immediate) v.Place(SlotPos(index));
        else v.SetTarget(SlotPos(index));
        line.Add(v);
        return v;
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
            candidates.Add(def);
        }
        if (futureScore >= furChangeStartScore) candidates.AddRange(ItemTable.For(kind, ItemCategory.Fur));
        if (candidates.Count == 0) return;

        int count = 1;
        if (futureScore >= multiDisguiseStartScore)
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
            float furSkill = Mathf.InverseLerp(furChangeStartScore, scoreForMaxDisguise, futureScore);
            float expected = furSkill * (disguise.DyeLevelCount - 1);
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
