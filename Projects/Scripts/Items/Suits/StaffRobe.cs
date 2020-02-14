using System;

namespace Server.Items.Suits
{
  public class StaffRobe : Item
  {
    private AccessLevel m_AccessLevel = AccessLevel.Player;
    [CommandProperty(AccessLevel.Counselor)]
    public AccessLevel AccessLevel
    {
      get => m_AccessLevel;
      set
      {
        m_AccessLevel = value;
        switch (value)
        {
          case AccessLevel.Player:
            ItemID = 0x204E;
            Hue = 0;
            break;
          case AccessLevel.Counselor:
            ItemID = 0x204F;
            Hue = 0x3;
            break;
          case AccessLevel.GameMaster:
            ItemID = 0x204F;
            Hue = 0x26;
            break;
          case AccessLevel.Seer:
            ItemID = 0x204F;
            Hue = 0x1D3;
            break;
          default:
            ItemID = 0x204F;
            Hue = 0x0;
            break;
        }
      }
    }
    [CommandProperty(AccessLevel.GameMaster, true)]
    public AccessLevel? PreviousAccessLevel { get; set; } = null;

    [Constructible(AccessLevel.GameMaster)]
    public StaffRobe() : base(0x204F)
    {
      Hue = 0x26;
      Weight = 1.0;
      Movable = false;
      LootType = LootType.Newbied;
      Layer = Layer.OuterTorso;
    }
    public StaffRobe(Serial serial) : base(serial) { }

    public override void Serialize(IGenericWriter writer)
    {
      base.Serialize(writer);
      writer.Write(m_AccessLevel.ToString());
      writer.Write(PreviousAccessLevel.HasValue);
      if (PreviousAccessLevel.HasValue)
        writer.Write(PreviousAccessLevel.Value.ToString());
    }
    public override void Deserialize(IGenericReader reader)
    {
      base.Deserialize(reader);
      m_AccessLevel = Enum.Parse<AccessLevel>(reader.ReadString());
      if (reader.ReadBool())
        PreviousAccessLevel = Enum.Parse<AccessLevel>(reader.ReadString());
    }

    public override bool OnEquip(Mobile from)
    {
      if (from.AccessLevel < AccessLevel)
        from.SendMessage("You may not wear this.");

      return from.AccessLevel >= AccessLevel;
    }
    public override void OnAdded(IEntity parent)
    {
      if (parent is Mobile mobile)
      {
        PreviousAccessLevel = mobile.AccessLevel;
        mobile.AccessLevel = AccessLevel;
      }

      base.OnAdded(parent);
    }
    public override void OnRemoved(IEntity parent)
    {
      if (parent is Mobile mobile && PreviousAccessLevel.HasValue)
      {
        mobile.AccessLevel = PreviousAccessLevel.Value;
        PreviousAccessLevel = null;
      }

      base.OnRemoved(parent);
    }
  }
}
