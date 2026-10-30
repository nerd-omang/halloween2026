using System.Collections.Generic;
using UnityEngine;

public class VisitorQueue : MonoBehaviour
{
    [SerializeField] Visitor kunmungchiPrefab;
    [SerializeField] Visitor omangiPrefab;
    [SerializeField] int disguiseStartScore = 3;
    [SerializeField] float disguiseChanceStart = 0.2f;
    [SerializeField] float disguiseChanceMax = 0.5f;
    [SerializeField] int scoreForMaxDisguise = 30;
    [SerializeField] int firstDisguisedOmangiScore = 5;
    [SerializeField] int sheetStartScore = 10;
    [SerializeField] float sheetChanceStart = 0.15f;
    [SerializeField] float sheetChanceMax = 0.35f;
    [SerializeField] int scoreForMaxSheet = 35;
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

        Visitor prefab = isOmangi ? omangiPrefab : kunmungchiPrefab;
        int index = line.Count;
        var v = Instantiate(prefab, SlotPos(visibleCount), Quaternion.identity, transform);
        if (disguised && v.TryGetComponent(out VisitorDisguise disguise))
        {
            disguise.Randomize(DisguiseElements(disguiseT, disguise.ElementCount), disguiseT);
        }

        float sheetT = Mathf.InverseLerp(sheetStartScore, scoreForMaxSheet, futureScore);
        float sheetChance = Mathf.Lerp(sheetChanceStart, sheetChanceMax, sheetT);
        if (futureScore >= sheetStartScore && Random.value < sheetChance && v.TryGetComponent(out GhostSheet sheet))
        {
            sheet.Wear();
        }

        if (immediate) v.Place(SlotPos(index));
        else v.SetTarget(SlotPos(index));
        line.Add(v);
        return v;
    }

    // How many disguise elements to wear. Early disguises are sloppy (mostly one element: just the
    // fur change or a single prop); by scoreForMaxChance they wear nearly everything.
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
