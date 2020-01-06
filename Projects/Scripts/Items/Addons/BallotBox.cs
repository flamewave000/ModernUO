using System;
using System.Collections.Generic;
using Server.Gumps;
using Server.Multis;
using Server.Network;
using Server.Prompts;

namespace Server.Items
{
  public class BallotBox : AddonComponent
  {
    public static readonly int MaxTopicLines = 6;

    [Constructible]
    public BallotBox() : base(0x9A8)
    {
      Topic = new string[0];
      Yes = new List<Mobile>();
      No = new List<Mobile>();
    }

    public BallotBox(Serial serial) : base(serial)
    {
    }

    public override int LabelNumber => 1041006; // a ballot box

    public string[] Topic{ get; private set; }

    public List<Mobile> Yes{ get; private set; }

    public List<Mobile> No{ get; private set; }

    public void ClearTopic()
    {
      Topic = new string[0];

      ClearVotes();
    }

    public void AddLineToTopic(string line)
    {
      if (Topic.Length >= MaxTopicLines)
        return;

      string[] newTopic = new string[Topic.Length + 1];
      Topic.CopyTo(newTopic, 0);
      newTopic[Topic.Length] = line;

      Topic = newTopic;

      ClearVotes();
    }

    public void ClearVotes()
    {
      Yes.Clear();
      No.Clear();
    }

    public bool IsOwner(Mobile from)
    {
      if (from.AccessLevel >= AccessLevel.GameMaster)
        return true;

      BaseHouse house = BaseHouse.FindHouseAt(this);
      return house?.IsOwner(from) == true;
    }

    public bool HasVoted(Mobile from) => Yes.Contains(from) || No.Contains(from);

    public override bool OnDragDrop(Mobile from, Item dropped)
    {
      SendLocalizedMessageTo(from, 500369); // I'm a ballot box, not a container!
      return false;
    }

    public override void OnDoubleClick(Mobile from)
    {
      if (!from.InRange(GetWorldLocation(), 2))
      {
        from.LocalOverheadMessage(MessageType.Regular, 0x3B2, 1019045); // I can't reach that.
      }
      else
      {
        bool isOwner = IsOwner(from);
        from.SendGump(new InternalGump(this, isOwner));
      }
    }

    public override void Serialize(IGenericWriter writer)
    {
      base.Serialize(writer);

      writer.WriteEncodedInt(0); // version

      writer.WriteEncodedInt(Topic.Length);

      for (int i = 0; i < Topic.Length; i++)
        writer.Write(Topic[i]);

      writer.Write(Yes, true);
      writer.Write(No, true);
    }

    public override void Deserialize(IGenericReader reader)
    {
      base.Deserialize(reader);

      int version = reader.ReadEncodedInt();

      Topic = new string[reader.ReadEncodedInt()];

      for (int i = 0; i < Topic.Length; i++)
        Topic[i] = reader.ReadString();

      Yes = reader.ReadStrongMobileList();
      No = reader.ReadStrongMobileList();
    }

    private class InternalGump : Gump
    {
      private BallotBox m_Box;

      public InternalGump(BallotBox box, bool isOwner) : base(110, 70)
      {
        m_Box = box;

        AddBackground(0, 0, 400, 350, 0xA28);

        if (isOwner)
          AddHtmlLocalized(0, 15, 400, 35, 1011000); // <center>Ballot Box Owner's Menu</center>
        else
          AddHtmlLocalized(0, 15, 400, 35, 1011001); // <center>Ballot Box -- Vote Here!</center>

        AddHtmlLocalized(0, 50, 400, 35, 1011002); // <center>Topic</center>

        int lineCount = box.Topic.Length;
        AddBackground(25, 90, 350, Math.Max(20 * lineCount, 20), 0x1400);

        for (int i = 0; i < lineCount; i++)
        {
          string line = box.Topic[i];

          if (!string.IsNullOrEmpty(line))
            AddLabelCropped(30, 90 + i * 20, 340, 20, 0x3E3, line);
        }

        int yesCount = box.Yes.Count;
        int noCount = box.No.Count;
        int totalVotes = yesCount + noCount;

        AddHtmlLocalized(0, 215, 400, 35, 1011003); // <center>votes</center>

        if (!isOwner)
          AddButton(20, 240, 0xFA5, 0xFA7, 3);
        AddHtmlLocalized(55, 242, 25, 35, 1011004); // aye:
        AddLabel(78, 242, 0x0, $"[{yesCount}]");

        if (!isOwner)
          AddButton(20, 275, 0xFA5, 0xFA7, 4);
        AddHtmlLocalized(55, 277, 25, 35, 1011005); // nay:
        AddLabel(78, 277, 0x0, $"[{noCount}]");

        if (totalVotes > 0)
        {
          AddImageTiled(130, 242, yesCount * 225 / totalVotes, 10, 0xD6);
          AddImageTiled(130, 277, noCount * 225 / totalVotes, 10, 0xD6);
        }

        AddButton(45, 305, 0xFA5, 0xFA7, 0);
        AddHtmlLocalized(80, 308, 40, 35, 1011008); // done

        if (isOwner)
        {
          AddButton(120, 305, 0xFA5, 0xFA7, 1);
          AddHtmlLocalized(155, 308, 100, 35, 1011006); // change topic

          AddButton(240, 305, 0xFA5, 0xFA7, 2);
          AddHtmlLocalized(275, 308, 300, 100, 1011007); // reset votes
        }
      }

      public override void OnResponse(NetState sender, RelayInfo info)
      {
        if (m_Box.Deleted || info.ButtonID == 0)
          return;

        Mobile from = sender.Mobile;

        if (from.Map != m_Box.Map || !from.InRange(m_Box.GetWorldLocation(), 2))
        {
          from.LocalOverheadMessage(MessageType.Regular, 0x3B2, 1019045); // I can't reach that.
          return;
        }

        bool isOwner = m_Box.IsOwner(from);

        switch (info.ButtonID)
        {
          case 1: // change topic
          {
            if (isOwner)
            {
              m_Box.ClearTopic();

              from.SendLocalizedMessage(500370, "",
                0x35); // Enter a line of text for your ballot, and hit ENTER. Hit ESC after the last line is entered.
              from.Prompt = new TopicPrompt(m_Box);
            }

            break;
          }
          case 2: // reset votes
          {
            if (isOwner)
            {
              m_Box.ClearVotes();
              from.SendLocalizedMessage(500371); // Votes zeroed out.
            }

            goto default;
          }
          case 3: // aye
          {
            if (!isOwner)
            {
              if (m_Box.HasVoted(from))
              {
                from.SendLocalizedMessage(500374); // You have already voted on this ballot.
              }
              else
              {
                m_Box.Yes.Add(from);
                from.SendLocalizedMessage(500373); // Your vote has been registered.
              }
            }

            goto default;
          }
          case 4: // nay
          {
            if (!isOwner)
            {
              if (m_Box.HasVoted(from))
              {
                from.SendLocalizedMessage(500374); // You have already voted on this ballot.
              }
              else
              {
                m_Box.No.Add(from);
                from.SendLocalizedMessage(500373); // Your vote has been registered.
              }
            }

            goto default;
          }
          default:
          {
            from.SendGump(new InternalGump(m_Box, isOwner));
            break;
          }
        }
      }
    }

    private class TopicPrompt : Prompt
    {
      private BallotBox m_Box;

      public TopicPrompt(BallotBox box) => m_Box = box;

      public override void OnResponse(Mobile from, string text)
      {
        if (m_Box.Deleted || !m_Box.IsOwner(from))
          return;

        if (from.Map != m_Box.Map || !from.InRange(m_Box.GetWorldLocation(), 2))
        {
          from.LocalOverheadMessage(MessageType.Regular, 0x3B2, 1019045); // I can't reach that.
          return;
        }

        m_Box.AddLineToTopic(text.TrimEnd());

        if (m_Box.Topic.Length < MaxTopicLines)
        {
          from.SendLocalizedMessage(500377, "", 0x35); // Next line or ESC to finish:
          from.Prompt = new TopicPrompt(m_Box);
        }
        else
        {
          from.SendLocalizedMessage(500376, "", 0x35); // Ballot entry complete.
          from.SendGump(new InternalGump(m_Box, true));
        }
      }

      public override void OnCancel(Mobile from)
      {
        if (m_Box.Deleted || !m_Box.IsOwner(from))
          return;

        if (from.Map != m_Box.Map || !from.InRange(m_Box.GetWorldLocation(), 2))
        {
          from.LocalOverheadMessage(MessageType.Regular, 0x3B2, 1019045); // I can't reach that.
          return;
        }

        from.SendLocalizedMessage(500376, "", 0x35); // Ballot entry complete.
        from.SendGump(new InternalGump(m_Box, true));
      }
    }
  }

  public class BallotBoxAddon : BaseAddon
  {
    public BallotBoxAddon()
    {
      AddComponent(new BallotBox(), 0, 0, 0);
    }

    public BallotBoxAddon(Serial serial) : base(serial)
    {
    }

    public override BaseAddonDeed Deed => new BallotBoxDeed();

    public override void Serialize(IGenericWriter writer)
    {
      base.Serialize(writer);

      writer.WriteEncodedInt(0); // version
    }

    public override void Deserialize(IGenericReader reader)
    {
      base.Deserialize(reader);

      int version = reader.ReadEncodedInt();
    }
  }

  public class BallotBoxDeed : BaseAddonDeed
  {
    [Constructible]
    public BallotBoxDeed()
    {
    }

    public BallotBoxDeed(Serial serial) : base(serial)
    {
    }

    public override BaseAddon Addon => new BallotBoxAddon();

    public override int LabelNumber => 1044327; // ballot box

    public override void Serialize(IGenericWriter writer)
    {
      base.Serialize(writer);

      writer.WriteEncodedInt(0); // version
    }

    public override void Deserialize(IGenericReader reader)
    {
      base.Deserialize(reader);

      int version = reader.ReadEncodedInt();
    }
  }
}
