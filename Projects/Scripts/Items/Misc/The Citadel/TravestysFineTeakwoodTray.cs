namespace Server.Items
{
  public class TravestysFineTeakwoodTray : Item
  {
    [Constructible]
    public TravestysFineTeakwoodTray() : base(Utility.Random(0x991, 2))
    {
    }

    public TravestysFineTeakwoodTray(Serial serial) : base(serial)
    {
    }

    public override int LabelNumber => 1075094; // Travesty's Fine Teakwood Tray

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
}