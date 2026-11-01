using System;

// A visitor's look as plain data: who they are, the worn items (one bit per ItemTable item, including the
// fur change and the ghost sheet) and the level of the fur change. Enough to rebuild the same look later
// (review, gallery, dress-up) and to send it to a server as a LookCode.
[Serializable]
public struct Outfit
{
    public VisitorKind kind;
    public uint items;
    public int dyeLevel;

    public bool Has(int bit) => (items & ItemTable.Mask(bit)) != 0;

    public void Set(int bit, bool on)
    {
        if (on) items |= ItemTable.Mask(bit);
        else items &= ~ItemTable.Mask(bit);
    }

    public bool fur
    {
        get => Has(ItemTable.FurBit);
        set => Set(ItemTable.FurBit, value);
    }

    // True while the visitor is covered: by the ghost sheet or by the pumpkin helmet (never both).
    // Setting it puts the ghost sheet on, or takes any cover off.
    public bool sheet
    {
        get => Has(ItemTable.SheetBit) || Has(ItemTable.PumpkinBit);
        set
        {
            Set(ItemTable.PumpkinBit, false);
            Set(ItemTable.SheetBit, value);
        }
    }

    // The cover is the pumpkin helmet, not the ghost sheet.
    public bool pumpkin
    {
        get => Has(ItemTable.PumpkinBit);
        set
        {
            Set(ItemTable.SheetBit, false);
            Set(ItemTable.PumpkinBit, value);
        }
    }
}

public struct MistakeRecord
{
    public Outfit outfit;
    public HandAction action;
}
