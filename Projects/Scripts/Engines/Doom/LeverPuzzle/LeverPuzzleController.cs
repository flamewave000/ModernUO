using System;
using System.Collections.Generic;
using System.Linq;
using Server.Mobiles;
using Server.Network;
using Server.Spells;

/*
	this is From me to you, Under no terms, Conditions...   K?  to apply you
	just simply Unpatch/delete, Stick these in, Same location.. Restart
	*/

namespace Server.Engines.Doom
{
  public class LeverPuzzleController : Item
  {
    private static bool installed;

    public static string[] Msgs =
    {
      "You are pinned down by the weight of the boulder!!!", // 0
      "A speeding rock hits you in the head!", // 1
      "OUCH!" // 2
    };
    /* font&hue for above msgs. index matches */

    public static int[][] MsgParams =
    {
      new[] { 0x66d, 3 },
      new[] { 0x66d, 3 },
      new[] { 0x34, 3 }
    };
    /* World data for items */

    public static int[][] TA =
    {
      new[] { 316, 64, 5 }, /* 3D Coords for levers */
      new[] { 323, 58, 5 },
      new[] { 332, 63, 5 },
      new[] { 323, 71, 5 },

      new[] { 324, 64 }, /* 2D Coords for standing regions */
      new[] { 316, 65 },
      new[] { 324, 58 },
      new[] { 332, 64 },
      new[] { 323, 72 },

      new[] { 468, 92, -1 }, new[] { 0x181D, 0x482 }, /* 3D coord, itemid+hue for L.R. teles */
      new[] { 469, 92, -1 }, new[] { 0x1821, 0x3fd },
      new[] { 470, 92, -1 }, new[] { 0x1825, 0x66d },

      new[] { 319, 70, 18 }, new[] { 0x12d8 }, /* 3D coord, itemid for statues */
      new[] { 329, 60, 18 }, new[] { 0x12d9 },

      new[] { 469, 96, 6 } /* 3D Coords for Fake Box */
    };

    /* CLILOC data for statue "correct souls" messages */

    public static int[] Statue_Msg = { 1050009, 1050007, 1050008, 1050008 };

    /* Exit & Enter locations for the lamp room */

    public static Point3D lr_Exit = new Point3D(353, 172, -1);
    public static Point3D lr_Enter = new Point3D(467, 96, -1);

    /* "Center" location in puzzle */

    public static Point3D lp_Center = new Point3D(324, 64, -1);

    /* Lamp Room Area */

    public static Rectangle2D lr_Rect = new Rectangle2D(465, 92, 10, 10);

    /* Lamp Room area Poison message data */

    public static int[][] PA =
    {
      new[] { 0, 0, 0xA6 },
      new[] { 1050001, 0x485, 0xAA },
      new[] { 1050003, 0x485, 0xAC },
      new[] { 1050056, 0x485, 0xA8 },
      new[] { 1050057, 0x485, 0xA4 },
      new[] { 1062091, 0x23F3, 0xAC }
    };

    public static Poison[] PA2 =
    {
      Poison.Lesser,
      Poison.Regular,
      Poison.Greater,
      Poison.Deadly,
      Poison.Lethal,
      Poison.Lethal
    };

    /* SOUNDS */

    private static int[] fs = { 0x144, 0x154 };
    private static int[] ms = { 0x144, 0x14B };
    private static int[] fs2 = { 0x13F, 0x154 };
    private static int[] ms2 = { 0x13F, 0x14B };
    private static int[] cs1 = { 0x244 };
    private static int[] exp = { 0x307 };
    private Timer l_Timer;
    private LampRoomBox m_Box;
    private Region m_LampRoom;

    private List<Item> m_Levers;
    private List<Item> m_Statues;
    private List<Item> m_Teles;
    private List<LeverPuzzleRegion> m_Tiles;

    private Timer m_Timer;

    public LeverPuzzleController() : base(0x1822)
    {
      Movable = false;
      Hue = 0x4c;
      installed = true;
      int i = 0;

      m_Levers = new List<Item>(); /* codes are 0x1 shifted left x # of bits, easily handled here */
      for (; i < 4; i++)
        m_Levers.Add(AddLeverPuzzlePart(TA[i], new LeverPuzzleLever((ushort)(1 << i), this)));

      m_Tiles = new List<LeverPuzzleRegion>();
      for (; i < 9; i++)
        m_Tiles.Add(new LeverPuzzleRegion(this, TA[i]));

      m_Teles = new List<Item>();
      for (; i < 15; i++)
        m_Teles.Add(AddLeverPuzzlePart(TA[i], new LampRoomTeleporter(TA[++i])));

      m_Statues = new List<Item>();
      for (; i < 19; i++)
        m_Statues.Add(AddLeverPuzzlePart(TA[i], new LeverPuzzleStatue(TA[++i], this)));

      if (!installed)
        Delete();
      else
        Enabled = true;

      m_Box = (LampRoomBox)AddLeverPuzzlePart(TA[i], new LampRoomBox(this));
      m_LampRoom = new LampRoomRegion(this);
      GenKey();
    }

    public LeverPuzzleController(Serial serial) : base(serial)
    {
    }

    [CommandProperty(AccessLevel.GameMaster)]
    public ushort MyKey{ get; set; }

    [CommandProperty(AccessLevel.GameMaster)]
    public ushort TheirKey{ get; set; }

    [CommandProperty(AccessLevel.GameMaster)]
    public bool Enabled{ get; set; }

    public Mobile Successful{ get; private set; }

    public bool CircleComplete
    {
      get /* OSI: all 5 must be occupied */
      {
        for (int i = 0; i < 5; i++)
          if (GetOccupant(i) == null)
            return false;
        return true;
      }
    }

    public static void Initialize()
    {
      CommandSystem.Register("GenLeverPuzzle", AccessLevel.Administrator, GenLampPuzzle_OnCommand);
    }

    [Usage("GenLeverPuzzle")]
    [Description("Generates lamp room and lever puzzle in doom.")]
    public static void GenLampPuzzle_OnCommand(CommandEventArgs e)
    {
      if (Map.Malas.GetItemsInRange(lp_Center, 0).OfType<LeverPuzzleController>().Any())
      {
        e.Mobile.SendMessage("Lamp room puzzle already exists: please delete the existing controller first ...");
        return;
      }

      e.Mobile.SendMessage("Generating Lamp Room puzzle...");
      new LeverPuzzleController().MoveToWorld(lp_Center, Map.Malas);

      if (!installed)
        e.Mobile.SendMessage("There was a problem generating the puzzle.");
      else
        e.Mobile.SendMessage("Lamp room puzzle successfully generated.");
    }

    public static Item AddLeverPuzzlePart(int[] Loc, Item newitem)
    {
      if (newitem?.Deleted != false)
        installed = false;
      else
        newitem.MoveToWorld(new Point3D(Loc[0], Loc[1], Loc[2]), Map.Malas);

      return newitem;
    }

    public override void OnDelete()
    {
      KillTimers();
      base.OnDelete();
    }

    public override void OnAfterDelete()
    {
      NukeItemList(m_Teles);
      NukeItemList(m_Statues);
      NukeItemList(m_Levers);

      m_LampRoom?.Unregister();
      if (m_Tiles != null)
        foreach (LeverPuzzleRegion region in m_Tiles)
          region.Unregister();
      if (m_Box?.Deleted == false)
        m_Box.Delete();
    }

    public static void NukeItemList(List<Item> list)
    {
      if (list?.Count > 0)
        foreach (Item item in list)
          if (item?.Deleted == false)
            item.Delete();
    }

    public virtual PlayerMobile GetOccupant(int index)
    {
      LeverPuzzleRegion region = m_Tiles[index];

      if (region?.Occupant?.Alive == true) return (PlayerMobile)region.Occupant;
      return null;
    }

    public virtual LeverPuzzleStatue GetStatue(int index)
    {
      LeverPuzzleStatue statue = (LeverPuzzleStatue)m_Statues[index];
      return statue?.Deleted == false ? statue : null;
    }

    public virtual LeverPuzzleLever GetLever(int index)
    {
      LeverPuzzleLever lever = (LeverPuzzleLever)m_Levers[index];

      return lever?.Deleted == false ? lever : null;
    }

    public virtual void PuzzleStatus(int message, string fstring = null)
    {
      for (int i = 0; i < 2; i++)
      {
        Item s;
        if ((s = GetStatue(i)) != null)
          s.PublicOverheadMessage(MessageType.Regular, 0x3B2, message, fstring);
      }
    }

    public virtual void ResetPuzzle()
    {
      PuzzleStatus(1062053);
      ResetLevers();
    }

    public virtual void ResetLevers()
    {
      for (int i = 0; i < 4; i++)
      {
        Item l;
        if ((l = GetLever(i)) != null)
        {
          l.ItemID = 0x108E;
          Effects.PlaySound(l.Location, Map, 0x3E8);
        }
      }

      TheirKey ^= TheirKey;
    }

    public virtual void KillTimers()
    {
      if (l_Timer?.Running == true) l_Timer.Stop();
      if (m_Timer?.Running == true) m_Timer.Stop();
    }

    public virtual void RemoveSuccessful()
    {
      Successful = null;
    }

    public virtual void LeverPulled(ushort code)
    {
      int correct = 0;

      KillTimers();

      /* if one bit in each of the four nibbles is set, this is false */

      if ((TheirKey = (ushort)(code | (TheirKey <<= 4))) < 0x0FFF)
      {
        l_Timer = Timer.DelayCall(TimeSpan.FromSeconds(30.0), ResetPuzzle);
        return;
      }

      if (!CircleComplete)
      {
        PuzzleStatus(1050004); // The circle is the key...
      }
      else
      {
        Mobile player;
        if (TheirKey == MyKey)
        {
          GenKey();
          if ((Successful = player = GetOccupant(0)) != null)
          {
            SendLocationEffect(lp_Center, 0x1153, 0, 60, 1);
            PlaySounds(lp_Center, cs1);

            Effects.SendBoltEffect(player, true);
            player.MoveToWorld(lr_Enter, Map.Malas);

            m_Timer = new LampRoomTimer(this);
            m_Timer.Start();
            Enabled = false;
          }
        }
        else
        {
          for (int i = 0; i < 16; i++) /* Count matching SET bits, ie correct codes */
            if ((MyKey >> i & 1) == 1 && (TheirKey >> i & 1) == 1)
              correct++;

          PuzzleStatus(Statue_Msg[correct], correct > 0 ? correct.ToString() : null);

          for (int i = 0; i < 5; i++)
            if ((player = GetOccupant(i)) != null)
              new RockTimer(player, this).Start();
        }
      }

      ResetLevers();
    }

    public virtual void GenKey() /* Shuffle & build key */
    {
      ushort[] CA = { 1, 2, 4, 8 };
      for (int i = 0; i < 4; i++)
      {
        int n = (n = Utility.Random(0, 3)) == i ? n & ~i : n;
        ushort tmp = CA[i];
        CA[i] = CA[n];
        CA[n] = tmp;
      }

      for (int i = 0; i < 4; MyKey = (ushort)(CA[i++] | (MyKey <<= 4)))
      {
      }
    }

    private static bool IsValidDamagable(Mobile m) =>
      m?.Deleted == false &&
      (m.Player && m.Alive ||
       m is BaseCreature bc && (bc.Controlled || bc.Summoned) && !bc.IsDeadBondedPet);

    public static void MoveMobileOut(Mobile m)
    {
      if (m != null)
      {
        if (m is PlayerMobile && !m.Alive)
          if (m.Corpse?.Deleted == false)
            m.Corpse.MoveToWorld(lr_Exit, Map.Malas);
        BaseCreature.TeleportPets(m, lr_Exit, Map.Malas);
        m.Location = lr_Exit;
        m.ProcessDelta();
      }
    }

    public static bool AniSafe(Mobile m) => m?.BodyMod == 0 && m.Alive && !TransformationSpellHelper.UnderTransformation(m);

    public static IEntity ZAdjustedIEFromMobile(Mobile m, int ZDelta) => new Entity(Serial.Zero, new Point3D(m.X, m.Y, m.Z + ZDelta), m.Map);

    public static void DoDamage(Mobile m, int min, int max, bool poison)
    {
      if (m?.Deleted == false && m.Alive)
      {
        int damage = Utility.Random(min, max);
        AOS.Damage(m, damage, poison ? 0 : 100, 0, 0, poison ? 100 : 0, 0);
      }
    }

    public static Point3D RandomPointIn(Point3D point, int range) => RandomPointIn(point.X - range, point.Y - range, range * 2, range * 2, point.Z);

    public static Point3D RandomPointIn(Rectangle2D rect, int z) => RandomPointIn(rect.X, rect.Y, rect.Height, rect.Width, z);

    public static Point3D RandomPointIn(int x, int y, int x2, int y2, int z) => new Point3D(Utility.Random(x, x2), Utility.Random(y, y2), z);

    public static void PlaySounds(Point3D location, int[] sounds)
    {
      foreach (int soundid in sounds)
        Effects.PlaySound(location, Map.Malas, soundid);
    }

    public static void PlayEffect(IEntity from, IEntity to, int itemid, int speed, bool explodes)
    {
      Effects.SendMovingParticles(from, to, itemid, speed, 0, true, explodes, 2, 0, 0);
    }

    public static void SendLocationEffect(IPoint3D p, int itemID, int speed, int duration, int hue)
    {
      Effects.SendPacket(p, Map.Malas, new LocationEffect(p, itemID, speed, duration, hue, 0));
    }

    public static void PlayerSendASCII(Mobile player, int index)
    {
      player.Send(new AsciiMessage(Serial.MinusOne, 0xFFFF, MessageType.Label, MsgParams[index][0],
        MsgParams[index][1], null, Msgs[index]));
    }

    /* I cant find any better way to send "speech" using fonts other than default */
    public static void POHMessage(Mobile from, int index)
    {
      Packet p = new AsciiMessage(from.Serial, from.Body, MessageType.Regular, MsgParams[index][0],
        MsgParams[index][1], from.Name, Msgs[index]);
      p.Acquire();
      foreach (NetState state in from.Map.GetClientsInRange(from.Location))
        state.Send(p);

      Packet.Release(p);
    }

    public override void Serialize(IGenericWriter writer)
    {
      base.Serialize(writer);
      writer.Write(0); // version
      writer.WriteItemList(m_Levers, true);
      writer.WriteItemList(m_Statues, true);
      writer.WriteItemList(m_Teles, true);
      writer.Write(m_Box);
    }

    public override void Deserialize(IGenericReader reader)
    {
      base.Deserialize(reader);

      int version = reader.ReadInt();

      m_Levers = reader.ReadStrongItemList();
      m_Statues = reader.ReadStrongItemList();
      m_Teles = reader.ReadStrongItemList();

      m_Box = reader.ReadItem() as LampRoomBox;

      m_Tiles = new List<LeverPuzzleRegion>();
      for (int i = 4; i < 9; i++)
        m_Tiles.Add(new LeverPuzzleRegion(this, TA[i]));

      m_LampRoom = new LampRoomRegion(this);
      Enabled = true;
      TheirKey = 0;
      MyKey = 0;
      GenKey();
    }

    public class RockTimer : Timer
    {
      private int Count;
      private LeverPuzzleController m_Controller;
      private Mobile m_Player;

      public RockTimer(Mobile player, LeverPuzzleController Controller)
        : base(TimeSpan.Zero, TimeSpan.FromSeconds(.25))
      {
        Count = 0;
        m_Player = player;
        m_Controller = Controller;
      }

      private int Rock() => 0x1363 + Utility.Random(0, 11);

      protected override void OnTick()
      {
        if (m_Player == null || m_Player.Map != Map.Malas)
        {
          Stop();
        }
        else
        {
          Count++;
          if (Count == 1) /* TODO consolidate */
          {
            m_Player.Paralyze(TimeSpan.FromSeconds(2));
            Effects.SendTargetEffect(m_Player, 0x11B7, 20, 10);
            PlayerSendASCII(m_Player, 0); // You are pinned down ...

            PlaySounds(m_Player.Location, !m_Player.Female ? fs : ms);
            PlayEffect(ZAdjustedIEFromMobile(m_Player, 50), m_Player, 0x11B7, 20, false);
          }
          else if (Count == 2)
          {
            DoDamage(m_Player, 80, 90, false);
            Effects.SendTargetEffect(m_Player, 0x36BD, 20, 10);
            PlaySounds(m_Player.Location, exp);
            PlayerSendASCII(m_Player, 1); // A speeding rock  ...

            if (AniSafe(m_Player)) m_Player.Animate(21, 10, 1, true, true, 0);
          }
          else if (Count == 3)
          {
            Stop();

            Effects.SendTargetEffect(m_Player, 0x36B0, 20, 10);
            PlayerSendASCII(m_Player, 1); // A speeding rock  ...
            PlaySounds(m_Player.Location, !m_Player.Female ? fs2 : ms2);

            int j = Utility.Random(6, 10);
            for (int i = 0; i < j; i++)
            {
              IEntity m_IEntity = new Entity(Serial.Zero, RandomPointIn(m_Player.Location, 10), m_Player.Map);

              List<Mobile> mobiles = m_IEntity.Map.GetMobilesInRange(m_IEntity.Location, 2).ToList();

              for (int k = 0; k < mobiles.Count; k++)
                if (IsValidDamagable(mobiles[k]) && mobiles[k] != m_Player)
                {
                  PlayEffect(m_Player, mobiles[k], Rock(), 8, true);
                  DoDamage(mobiles[k], 25, 30, false);

                  if (mobiles[k].Player) POHMessage(mobiles[k], 2); // OUCH!
                }

              PlayEffect(m_Player, m_IEntity, Rock(), 8, false);
            }
          }
        }
      }
    }

    public class LampRoomKickTimer : Timer
    {
      private Mobile m;

      public LampRoomKickTimer(Mobile player)
        : base(TimeSpan.FromSeconds(.25)) =>
        m = player;

      protected override void OnTick()
      {
        MoveMobileOut(m);
      }
    }

    public class LampRoomTimer : Timer
    {
      public int level;
      public LeverPuzzleController m_Controller;
      public int ticks;

      public LampRoomTimer(LeverPuzzleController controller)
        : base(TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(5.0))
      {
        level = 0;
        ticks = 0;
        m_Controller = controller;
      }

      protected override void OnTick()
      {
        ticks++;
        List<Mobile> mobiles = m_Controller.m_LampRoom.GetMobiles();

        if (ticks >= 71 || m_Controller.m_LampRoom.GetPlayerCount() == 0)
        {
          foreach (Mobile mobile in mobiles)
            if (mobile?.Deleted == false && !mobile.IsDeadBondedPet)
              mobile.Kill();
          m_Controller.Enabled = true;
          Stop();
        }
        else
        {
          if (ticks % 12 == 0) level++;
          foreach (Mobile mobile in mobiles)
            if (IsValidDamagable(mobile))
            {
              if (ticks % 2 == 0 && level == 5)
              {
                if (mobile.Player)
                {
                  mobile.Say(1062092);
                  if (AniSafe(mobile)) mobile.Animate(32, 5, 1, true, false, 0);
                }

                DoDamage(mobile, 15, 20, true);
              }

              if (Utility.Random((int)(level & ~0xfffffffc), 3) == 3)
                mobile.ApplyPoison(mobile, PA2[level]);
              if (ticks % 12 == 0 && level > 0 && mobile.Player)
                mobile.SendLocalizedMessage(PA[level][0], null, PA[level][1]);
            }

          for (int i = 0; i <= level; i++)
            SendLocationEffect(RandomPointIn(lr_Rect, -1), 0x36B0, Utility.Random(150, 200), 0, PA[level][2]);
        }
      }
    }
  }
}
