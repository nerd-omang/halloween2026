using System.Collections.Generic;
using UnityEngine;

public class VisitorQueue : MonoBehaviour
{
    [SerializeField] Visitor kunmungchiPrefab;
    [SerializeField] Visitor omangiPrefab;
    [SerializeField] int visibleCount = 8;
    [SerializeField] float frontZ = 2.6f;
    [SerializeField] float spacing = 1.1f;
    [SerializeField] float lateralStep = 0.7f;
    [SerializeField] float groundY = 0.45f;
    [SerializeField] float omangiChanceStart = 0.25f;
    [SerializeField] float omangiChanceMax = 0.55f;
    [SerializeField] int scoreForMaxChance = 40;

    readonly List<Visitor> line = new List<Visitor>();

    public Visitor Front => line.Count > 0 ? line[0] : null;
    public bool FrontReady => Front != null && Front.Arrived;

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
        while (line.Count < visibleCount)
        {
            Spawn(0, true);
        }
    }

    Visitor Spawn(int score, bool immediate)
    {
        float t = Mathf.Clamp01(score / (float)scoreForMaxChance);
        float chance = Mathf.Lerp(omangiChanceStart, omangiChanceMax, t);
        var prefab = Random.value < chance ? omangiPrefab : kunmungchiPrefab;
        int index = line.Count;
        var v = Instantiate(prefab, SlotPos(visibleCount), Quaternion.identity, transform);
        if (immediate) v.Place(SlotPos(index));
        else v.SetTarget(SlotPos(index));
        line.Add(v);
        return v;
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
