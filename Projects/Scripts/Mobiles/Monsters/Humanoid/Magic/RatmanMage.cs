using Server.Misc;

namespace Server.Mobiles
{
  public class RatmanMage : BaseCreature
  {
    [Constructible]
    public RatmanMage() : base(AIType.AI_Mage, FightMode.Closest, 10, 1, 0.2, 0.4)
    {
      Name = NameList.RandomName("ratman");
      Body = 0x8F;
      BaseSoundID = 437;

      SetStr(146, 180);
      SetDex(101, 130);
      SetInt(186, 210);

      SetHits(88, 108);

      SetDamage(7, 14);

      SetDamageType(ResistanceType.Physical, 100);

      SetResistance(ResistanceType.Physical, 40, 45);
      SetResistance(ResistanceType.Fire, 10, 20);
      SetResistance(ResistanceType.Cold, 10, 20);
      SetResistance(ResistanceType.Poison, 10, 20);
      SetResistance(ResistanceType.Energy, 10, 20);

      SetSkill(SkillName.EvalInt, 70.1, 80.0);
      SetSkill(SkillName.Magery, 70.1, 80.0);
      SetSkill(SkillName.MagicResist, 65.1, 90.0);
      SetSkill(SkillName.Tactics, 50.1, 75.0);
      SetSkill(SkillName.Wrestling, 50.1, 75.0);

      Fame = 7500;
      Karma = -7500;

      VirtualArmor = 44;

      PackReg(6);

      if (0.02 > Utility.RandomDouble())
        PackStatue();
    }

    public RatmanMage(Serial serial) : base(serial)
    {
    }

    public override string CorpseName => "a glowing ratman corpse";
    public override InhumanSpeech SpeechType => InhumanSpeech.Ratman;

    public override bool CanRummageCorpses => true;
    public override int Meat => 1;
    public override int Hides => 8;
    public override HideType HideType => HideType.Spined;

    public override void GenerateLoot()
    {
      AddLoot(LootPack.Rich);
      AddLoot(LootPack.LowScrolls);
    }

    public override void Serialize(IGenericWriter writer)
    {
      base.Serialize(writer);
      writer.Write(0);
    }

    public override void Deserialize(IGenericReader reader)
    {
      base.Deserialize(reader);
      int version = reader.ReadInt();

      if (Body == 42)
      {
        Body = 0x8F;
        Hue = 0;
      }
    }
  }
}