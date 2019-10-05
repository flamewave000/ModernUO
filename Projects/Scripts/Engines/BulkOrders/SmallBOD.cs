using System;
using Server.Items;
using Server.Mobiles;

namespace Server.Engines.BulkOrders
{
  public abstract class SmallBOD : BaseBOD
  {
    private int m_AmountCur;
    private int m_Number;

    public SmallBOD(int hue, int amountCur, int amountMax, Type type, int number, int graphic, bool requireExeptional,
      BulkMaterialType material) : base(hue, amountMax, requireExeptional, material)
    {
      Type = type;
      Graphic = graphic;
      m_AmountCur = amountCur;
      m_Number = number;
    }

    public SmallBOD()
    {
    }

    public SmallBOD(Serial serial) : base(serial)
    {
    }

    [CommandProperty(AccessLevel.GameMaster)]
    public int AmountCur
    {
      get => m_AmountCur;
      set
      {
        m_AmountCur = value;
        InvalidateProperties();
      }
    }

    [CommandProperty(AccessLevel.GameMaster)]
    public Type Type{ get; set; }

    [CommandProperty(AccessLevel.GameMaster)]
    public int Number
    {
      get => m_Number;
      set
      {
        m_Number = value;
        InvalidateProperties();
      }
    }

    [CommandProperty(AccessLevel.GameMaster)]
    public int Graphic{ get; set; }

    [CommandProperty(AccessLevel.GameMaster)]
    public override bool Complete => m_AmountCur == AmountMax;

    public override int LabelNumber => 1045151; // a bulk order deed

    public override void GetProperties(ObjectPropertyList list)
    {
      base.GetProperties(list);

      list.Add(1060654); // small bulk order

      if (RequireExceptional)
        list.Add(1045141); // All items must be exceptional.

      if (Material != BulkMaterialType.None)
        list.Add(SmallBODGump.GetMaterialNumberFor(Material)); // All items must be made with x material.

      list.Add(1060656, AmountMax.ToString()); // amount to make: ~1_val~
      list.Add(1060658, "#{0}\t{1}", m_Number, m_AmountCur); // ~1_val~: ~2_val~
    }

    public override void OnDoubleClick(Mobile from)
    {
      if (IsChildOf(from.Backpack) || InSecureTrade || RootParent is PlayerVendor)
        from.SendGump(new SmallBODGump(from, this));
      else
        from.SendLocalizedMessage(1045156); // You must have the deed in your backpack to use it.
    }

    public override void OnDoubleClickNotAccessible(Mobile from)
    {
      OnDoubleClick(from);
    }

    public override void OnDoubleClickSecureTrade(Mobile from)
    {
      OnDoubleClick(from);
    }

    public static BulkMaterialType GetMaterial(CraftResource resource)
    {
      switch (resource)
      {
        case CraftResource.DullCopper: return BulkMaterialType.DullCopper;
        case CraftResource.ShadowIron: return BulkMaterialType.ShadowIron;
        case CraftResource.Copper: return BulkMaterialType.Copper;
        case CraftResource.Bronze: return BulkMaterialType.Bronze;
        case CraftResource.Gold: return BulkMaterialType.Gold;
        case CraftResource.Agapite: return BulkMaterialType.Agapite;
        case CraftResource.Verite: return BulkMaterialType.Verite;
        case CraftResource.Valorite: return BulkMaterialType.Valorite;
        case CraftResource.SpinedLeather: return BulkMaterialType.Spined;
        case CraftResource.HornedLeather: return BulkMaterialType.Horned;
        case CraftResource.BarbedLeather: return BulkMaterialType.Barbed;
      }

      return BulkMaterialType.None;
    }

    public override void EndCombine(Mobile from, Item item)
    {
      Type objectType = item.GetType();

        if (m_AmountCur >= AmountMax)
        {
          from.SendLocalizedMessage(
            1045166); // The maximum amount of requested items have already been combined to this deed.
        }
        else if (Type == null || objectType != Type && !objectType.IsSubclassOf(Type) ||
                 !(item is BaseWeapon) && !(item is BaseArmor) && !(item is BaseClothing))
        {
          from.SendLocalizedMessage(1045169); // The item is not in the request.
        }
        else
        {
          BaseArmor armor = item as BaseArmor;
          BaseClothing clothing = item as BaseClothing;

          BulkMaterialType material = GetMaterial(armor?.Resource ?? clothing?.Resource ?? CraftResource.None);

          if (Material >= BulkMaterialType.DullCopper && Material <= BulkMaterialType.Valorite &&
              material != Material)
          {
            from.SendLocalizedMessage(1045168); // The item is not made from the requested ore.
          }
          else if (Material >= BulkMaterialType.Spined && Material <= BulkMaterialType.Barbed &&
                   material != Material)
          {
            from.SendLocalizedMessage(1049352); // The item is not made from the requested leather type.
          }
          else
          {
            bool isExceptional;

            if (item is BaseWeapon weapon)
              isExceptional = weapon.Quality == WeaponQuality.Exceptional;
            else if (armor != null)
              isExceptional = armor.Quality == ArmorQuality.Exceptional;
            else
              isExceptional = clothing.Quality == ClothingQuality.Exceptional;

            if (RequireExceptional && !isExceptional)
            {
              from.SendLocalizedMessage(1045167); // The item must be exceptional.
            }
            else
            {
              item.Delete();
              ++AmountCur;

              from.SendLocalizedMessage(1045170); // The item has been combined with the deed.
              from.SendGump(new SmallBODGump(from, this));

              if (m_AmountCur < AmountMax)
                BeginCombine(from);
            }
          }
        }
    }

    public override void Serialize(GenericWriter writer)
    {
      base.Serialize(writer);

      writer.Write(0); // version

      writer.Write(m_AmountCur);
      writer.Write(Type == null ? null : Type.FullName);
      writer.Write(m_Number);
      writer.Write(Graphic);
    }

    public override void Deserialize(GenericReader reader)
    {
      base.Deserialize(reader);

      int version = reader.ReadInt();

      switch (version)
      {
        case 0:
        {
          m_AmountCur = reader.ReadInt();

          string type = reader.ReadString();

          if (type != null)
            Type = AssemblyHandler.FindTypeByFullName(type);

          m_Number = reader.ReadInt();
          Graphic = reader.ReadInt();
          break;
        }
      }
    }
  }
}
