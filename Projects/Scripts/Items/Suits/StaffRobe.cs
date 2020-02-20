using System;

namespace Server.Items.Suits
{
  public class StaffRobe : BaseSuit
  {
    public static int GetHueForAccess(AccessLevel accessLevel)
    {
      switch (accessLevel)
      {
        default:
          return 0;
        case AccessLevel.Counselor:
          return 0x3;
        case AccessLevel.GameMaster:
          return 0x26;
        case AccessLevel.Seer:
          return 0x1D3;
        case AccessLevel.Administrator:
          return 0x4AB;
        case AccessLevel.Developer:
          return 0x480;
        case AccessLevel.Owner:
          return 0x496;
      }
    }

    [Constructible]
    public StaffRobe() : base(AccessLevel.Counselor, 0x0, 0x204F)
    {
      Name = "Staff Robe";
    }

    public StaffRobe(Serial serial) : base(serial)
    {
    }
    public override bool OnEquip(Mobile from)
    {
      if (!base.OnEquip(from)) return false;
      Hue = GetHueForAccess(from.AccessLevel);
      return true;
    }

    public override void Serialize(IGenericWriter writer)
    {
      base.Serialize(writer);
      writer.Write(0); // version
    }

    public override void Deserialize(IGenericReader reader)
    {
      base.Deserialize(reader);
      int version = reader.ReadInt();
    }
  }


  public class AccessRobe : Item
  {
    private AccessLevel m_AccessLevel = AccessLevel.Player;
    [CommandProperty(AccessLevel.Player)]
    public AccessLevel AccessLevel
    {
      get => m_AccessLevel;
      set
      {
        // If the acess level is higher than the player is originally allowed to be. Ye shalt not stand above thy station!
        if (PreviousAccessLevel != null && value > PreviousAccessLevel)
        {
          (Parent as Mobile).SendMessage($"Ye shalt not stand above thy station! Thou art a {PreviousAccessLevel.Value.ToString()} and cannot be a {value.ToString()}!");
          value = PreviousAccessLevel.Value;
        }
        m_AccessLevel = value;
        Hue = StaffRobe.GetHueForAccess(value);
        ItemID = value > AccessLevel.Player ? 0x204F : 0x2687;//0x204E;
        Name = value > AccessLevel.Player ? value.ToString() + " Robe" : "Death Shroud";
        if (Parent != null && Parent is Mobile mobile)
          mobile.AccessLevel = value;
      }
    }
    [CommandProperty(AccessLevel.Counselor, true)]
    public AccessLevel? PreviousAccessLevel { get; set; } = null;

    [Constructible]
    public AccessRobe() : base(0x204F)
    {
      Hue = 0x26;
      Weight = 1.0;
      Movable = true;
      LootType = LootType.Newbied;
      Layer = Layer.OuterTorso;
    }
    public AccessRobe(Serial serial) : base(serial) { }

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
        from.SendMessage("You may not wear this. Thou shalt not stand above thy station!");
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
