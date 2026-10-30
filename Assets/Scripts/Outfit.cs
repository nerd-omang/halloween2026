using System;

// A visitor's look as plain data: who they are plus the VisitorDisguise mask (bit 0 = fur change,
// bit i = props[i - 1]), the fur change level and whether a ghost sheet is worn.
// Enough to rebuild the same look later (review, gallery, dress-up).
[Serializable]
public struct Outfit
{
    public VisitorKind kind;
    public int mask;
    public int dyeLevel;
    public bool sheet;
}

public struct MistakeRecord
{
    public Outfit outfit;
    public HandAction action;
}
