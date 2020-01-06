namespace Server.Items
{
  [Flippable(0x1c0c, 0x1c0d)]
  public class StuddedBustierArms : BaseArmor
  {
    [Constructible]
    public StuddedBustierArms() : base(0x1C0C) => Weight = 1.0;

    public StuddedBustierArms(Serial serial) : base(serial)
    {
    }

    public override int BasePhysicalResistance => 2;
    public override int BaseFireResistance => 4;
    public override int BaseColdResistance => 3;
    public override int BasePoisonResistance => 3;
    public override int BaseEnergyResistance => 4;

    public override int InitMinHits => 35;
    public override int InitMaxHits => 45;

    public override int AosStrReq => 35;
    public override int OldStrReq => 35;

    public override int ArmorBase => 16;

    public override ArmorMaterialType MaterialType => ArmorMaterialType.Studded;
    public override CraftResource DefaultResource => CraftResource.RegularLeather;

    public override ArmorMeditationAllowance DefMedAllowance => ArmorMeditationAllowance.Half;

    public override bool AllowMaleWearer => false;

    public override void Serialize(IGenericWriter writer)
    {
      base.Serialize(writer);
      writer.Write(0);
    }

    public override void Deserialize(IGenericReader reader)
    {
      base.Deserialize(reader);
      int version = reader.ReadInt();
    }
  }
}