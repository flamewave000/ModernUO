using System;
using System.Collections.Generic;
using System.Linq;
using Server.Accounting;
using Server.ContextMenus;
using Server.Ethics;
using Server.Guilds;
using Server.Gumps;
using Server.Items;
using Server.Misc;
using Server.Mobiles;
using Server.Multis.Deeds;
using Server.Network;
using Server.Regions;
using Server.Targeting;

namespace Server.Multis
{
  public abstract class BaseHouse : BaseMulti
  {
    public const int MaxCoOwners = 15;

    public const bool DecayEnabled = true;

    public const int MaximumBarkeepCount = 2;

    private static readonly Dictionary<Mobile, List<BaseHouse>> m_Table = new Dictionary<Mobile, List<BaseHouse>>();

    private DecayLevel m_LastDecayLevel;

    private Mobile m_Owner;

    private bool m_Public;

    private HouseRegion m_Region;

    private Point3D m_RelativeBanLocation;
    private TrashBarrel m_Trash;

    public BaseHouse(int multiID, Mobile owner, int MaxLockDown, int MaxSecure) : base(multiID)
    {
      AllHouses.Add(this);

      LastRefreshed = DateTime.UtcNow;

      BuiltOn = DateTime.UtcNow;
      LastTraded = DateTime.MinValue;

      Doors = new List<BaseDoor>();
      LockDowns = new List<Item>();
      Secures = new List<SecureInfo>();
      Addons = new List<Item>();

      CoOwners = new List<Mobile>();
      Friends = new List<Mobile>();
      Bans = new List<Mobile>();
      Access = new List<Mobile>();

      VendorRentalContracts = new List<VendorRentalContract>();
      InternalizedVendors = new List<Mobile>();

      m_Owner = owner;

      MaxLockDowns = MaxLockDown;
      MaxSecures = MaxSecure;

      m_RelativeBanLocation = BaseBanLocation;

      UpdateRegion();

      if (owner != null)
      {
        if (!m_Table.TryGetValue(owner, out List<BaseHouse> list))
          m_Table[owner] = list = new List<BaseHouse>();

        list.Add(this);
      }

      Movable = false;
    }

    public BaseHouse(Serial serial) : base(serial)
    {
      AllHouses.Add(this);
    }

    public static bool NewVendorSystem // Is new player vendor system enabled?
      => Core.AOS;

    public static int MaxFriends => !Core.AOS ? 50 : 140;
    public static int MaxBans => !Core.AOS ? 50 : 140;

    [CommandProperty(AccessLevel.GameMaster)]
    public DateTime LastRefreshed{ get; set; }

    [CommandProperty(AccessLevel.GameMaster)]
    public bool RestrictDecay{ get; set; }

    public virtual TimeSpan DecayPeriod => TimeSpan.FromDays(5.0);

    public virtual DecayType DecayType
    {
      get
      {
        if (RestrictDecay || !DecayEnabled || DecayPeriod == TimeSpan.Zero)
          return DecayType.Ageless;

        if (m_Owner == null)
          return Core.AOS ? DecayType.Condemned : DecayType.ManualRefresh;

        if (!(m_Owner.Account is Account acct))
          return Core.AOS ? DecayType.Condemned : DecayType.ManualRefresh;

        if (acct.AccessLevel >= AccessLevel.GameMaster)
          return DecayType.Ageless;

        for (int i = 0; i < acct.Length; ++i)
        {
          Mobile mob = acct[i];

          if (mob?.AccessLevel >= AccessLevel.GameMaster)
            return DecayType.Ageless;
        }

        if (!Core.AOS)
          return DecayType.ManualRefresh;

        if (acct.Inactive)
          return DecayType.Condemned;

        List<BaseHouse> allHouses = new List<BaseHouse>();

        for (int i = 0; i < acct.Length; ++i)
        {
          Mobile mob = acct[i];

          if (mob != null)
            allHouses.AddRange(GetHouses(mob));
        }

        BaseHouse newest = null;

        for (int i = 0; i < allHouses.Count; ++i)
        {
          BaseHouse check = allHouses[i];

          if (newest == null || IsNewer(check, newest))
            newest = check;
        }

        return this == newest ? DecayType.AutoRefresh : DecayType.ManualRefresh;
      }
    }

    public virtual bool CanDecay
    {
      get
      {
        DecayType type = DecayType;

        return type == DecayType.Condemned || type == DecayType.ManualRefresh;
      }
    }

    [CommandProperty(AccessLevel.GameMaster)]
    public virtual DecayLevel DecayLevel
    {
      get
      {
        DecayLevel result;

        if (!CanDecay)
        {
          if (DynamicDecay.Enabled)
            ResetDynamicDecay();

          LastRefreshed = DateTime.UtcNow;
          result = DecayLevel.Ageless;
        }
        else if (DynamicDecay.Enabled)
        {
          DecayLevel stage = m_CurrentStage;

          if (stage == DecayLevel.Ageless || DynamicDecay.Decays(stage) && NextDecayStage <= DateTime.UtcNow)
            SetDynamicDecay(++stage);

          if (stage == DecayLevel.Collapsed && (HasRentedVendors || VendorInventories.Count > 0))
            result = DecayLevel.DemolitionPending;
          else
            result = stage;
        }
        else
        {
          result = GetOldDecayLevel();
        }

        if (result != m_LastDecayLevel)
        {
          m_LastDecayLevel = result;

          if (Sign?.GettingProperties == false)
            Sign.InvalidateProperties();
        }

        return result;
      }
    }

    public virtual TimeSpan RestrictedPlacingTime => TimeSpan.FromHours(1.0);

    [CommandProperty(AccessLevel.GameMaster)]
    public virtual double BonusStorageScalar => Core.ML ? 1.2 : 1.0;

    public virtual bool IsAosRules => Core.AOS;

    public virtual bool IsActive => true;

    public bool HasPersonalVendors => PlayerVendors.Any(vendor => !(vendor is RentedVendor));

    public bool HasRentedVendors => PlayerVendors.OfType<RentedVendor>().Any();

    #region Mondain's Legacy

    public bool HasAddonContainers => Addons.OfType<BaseAddonContainer>().Any();

    #endregion

    public static List<BaseHouse> AllHouses{ get; } = new List<BaseHouse>();

    public abstract Rectangle2D[] Area{ get; }
    public abstract Point3D BaseBanLocation{ get; }

    public override bool Decays => false;

    [CommandProperty(AccessLevel.GameMaster)]
    public Mobile Owner
    {
      get => m_Owner;
      set
      {
        if (m_Owner != null)
        {
          if (!m_Table.TryGetValue(m_Owner, out List<BaseHouse> list))
            m_Table[m_Owner] = list = new List<BaseHouse>();

          list.Remove(this);
          m_Owner.Delta(MobileDelta.Noto);
        }

        m_Owner = value;

        if (m_Owner != null)
        {
          if (!m_Table.TryGetValue(m_Owner, out List<BaseHouse> list))
            m_Table[m_Owner] = list = new List<BaseHouse>();

          list.Add(this);
          m_Owner.Delta(MobileDelta.Noto);
        }

        Sign?.InvalidateProperties();
      }
    }

    [CommandProperty(AccessLevel.GameMaster)]
    public int Visits{ get; set; }

    [CommandProperty(AccessLevel.GameMaster)]
    public bool Public
    {
      get => m_Public;
      set
      {
        if (m_Public != value)
        {
          m_Public = value;

          if (!m_Public) // Privatizing the house, change to brass sign
            ChangeSignType(0xBD2);

          Sign?.InvalidateProperties();
        }
      }
    }

    [CommandProperty(AccessLevel.GameMaster)]
    public int MaxSecures{ get; set; }

    [CommandProperty(AccessLevel.GameMaster)]
    public Point3D BanLocation
    {
      get
      {
        if (m_Region != null)
          return m_Region.GoLocation;

        Point3D rel = m_RelativeBanLocation;
        return new Point3D(X + rel.X, Y + rel.Y, Z + rel.Z);
      }
      set => RelativeBanLocation = new Point3D(value.X - X, value.Y - Y, value.Z - Z);
    }

    [CommandProperty(AccessLevel.GameMaster)]
    public Point3D RelativeBanLocation
    {
      get => m_RelativeBanLocation;
      set
      {
        m_RelativeBanLocation = value;

        if (m_Region != null)
          m_Region.GoLocation = new Point3D(X + value.X, Y + value.Y, Z + value.Z);
      }
    }

    [CommandProperty(AccessLevel.GameMaster)]
    public int MaxLockDowns{ get; set; }

    public Region Region => m_Region;
    public List<Mobile> CoOwners{ get; set; }

    public List<Mobile> Friends{ get; set; }

    public List<Mobile> Access{ get; set; }

    public List<Mobile> Bans{ get; set; }

    public List<BaseDoor> Doors{ get; set; }

    public int LockDownCount
    {
      get
      {
        int count = GetLockdowns();

        if (Secures != null)
          for (int i = 0; i < Secures.Count; ++i)
          {
            SecureInfo info = Secures[i];

            if (info.Item.Deleted)
              continue;
            if (info.Item is StrongBox)
              count += 1;
            else
              count += 125;
          }

        return count;
      }
    }

    public int SecureCount => Secures?.Where(info => !info.Item.Deleted).Count(info => !(info.Item is StrongBox)) ?? 0;

    public List<Item> Addons{ get; set; }

    public List<Item> LockDowns{ get; private set; }

    public List<SecureInfo> Secures{ get; private set; }

    public HouseSign Sign{ get; set; }

    public List<PlayerVendor> PlayerVendors{ get; } = new List<PlayerVendor>();

    public List<PlayerBarkeeper> PlayerBarkeepers{ get; } = new List<PlayerBarkeeper>();

    public List<VendorRentalContract> VendorRentalContracts{ get; private set; }

    public List<VendorInventory> VendorInventories{ get; } = new List<VendorInventory>();

    public List<RelocatedEntity> RelocatedEntities{ get; } = new List<RelocatedEntity>();

    public MovingCrate MovingCrate{ get; set; }

    public List<Mobile> InternalizedVendors{ get; private set; }

    public DateTime BuiltOn{ get; set; }

    public DateTime LastTraded{ get; set; }

    public virtual HousePlacementEntry ConvertEntry => null;
    public virtual int ConvertOffsetX => 0;
    public virtual int ConvertOffsetY => 0;
    public virtual int ConvertOffsetZ => 0;

    public virtual int DefaultPrice => 0;

    [CommandProperty(AccessLevel.GameMaster)]
    public int Price{ get; set; }

    public static void Decay_OnTick()
    {
      for (int i = 0; i < AllHouses.Count; ++i)
        AllHouses[i].CheckDecay();
    }

    public bool IsNewer(BaseHouse check, BaseHouse house)
    {
      DateTime checkTime = check.LastTraded > check.BuiltOn ? check.LastTraded : check.BuiltOn;
      DateTime houseTime = house.LastTraded > house.BuiltOn ? house.LastTraded : house.BuiltOn;

      return checkTime > houseTime;
    }

    public DecayLevel GetOldDecayLevel()
    {
      TimeSpan timeAfterRefresh = DateTime.UtcNow - LastRefreshed;
      int percent = (int)(timeAfterRefresh.Ticks * 1000 / DecayPeriod.Ticks);

      if (percent >= 1000) // 100.0%
        return HasRentedVendors || VendorInventories.Count > 0 ? DecayLevel.DemolitionPending : DecayLevel.Collapsed;
      if (percent >= 950) // 95.0% - 99.9%
        return DecayLevel.IDOC;
      if (percent >= 750) // 75.0% - 94.9%
        return DecayLevel.Greatly;
      if (percent >= 500) // 50.0% - 74.9%
        return DecayLevel.Fairly;
      if (percent >= 250) // 25.0% - 49.9%
        return DecayLevel.Somewhat;
      if (percent >= 005) // 00.5% - 24.9%
        return DecayLevel.Slightly;

      return DecayLevel.LikeNew;
    }

    public virtual bool RefreshDecay()
    {
      if (DecayType == DecayType.Condemned)
        return false;

      DecayLevel oldLevel = DecayLevel;

      LastRefreshed = DateTime.UtcNow;

      if (DynamicDecay.Enabled)
        ResetDynamicDecay();

      Sign?.InvalidateProperties();

      return oldLevel > DecayLevel.LikeNew;
    }

    public virtual bool CheckDecay()
    {
      if (!Deleted && DecayLevel == DecayLevel.Collapsed)
      {
        Timer.DelayCall(TimeSpan.Zero, Decay_Sandbox);
        return true;
      }

      return false;
    }

    public virtual void KillVendors()
    {
      foreach (PlayerVendor vendor in PlayerVendors.ToList())
        vendor.Destroy(true);

      foreach (PlayerBarkeeper barkeeper in PlayerBarkeepers.ToList())
        barkeeper.Delete();
    }

    public virtual void Decay_Sandbox()
    {
      if (Deleted)
        return;

      if (Core.ML)
        new TempNoHousingRegion(this, null);

      KillVendors();
      Delete();
    }

    public virtual HousePlacementEntry GetAosEntry() => HousePlacementEntry.Find(this);

    public virtual int GetAosMaxSecures() => (int)(GetAosEntry()?.Storage ?? 0 * BonusStorageScalar);

    public virtual int GetAosMaxLockdowns() => (int)(GetAosEntry()?.Lockdowns ?? 0 * BonusStorageScalar);

    public virtual int GetAosCurSecures(out int fromSecures, out int fromVendors, out int fromLockdowns,
      out int fromMovingCrate)
    {
      fromSecures = 0;
      fromVendors = 0;
      fromLockdowns = 0;
      fromMovingCrate = 0;

      List<SecureInfo> list = Secures;

      if (list != null)
      {
        fromSecures += list.Sum(si => si.Item.TotalItems);
        fromLockdowns += list.Count;
      }

      fromLockdowns += GetLockdowns();

      if (!NewVendorSystem)
        fromVendors += PlayerVendors.Where(vendor => vendor.Backpack != null).Sum(vendor => vendor.Backpack.TotalItems);

      if (MovingCrate != null)
      {
        fromMovingCrate += MovingCrate.TotalItems;

        foreach (Item item in MovingCrate.Items)
          if (item is PackingBox)
            fromMovingCrate--;
      }

      return fromSecures + fromVendors + fromLockdowns + fromMovingCrate;
    }

    public bool InRange(IPoint2D from, int range) =>
      Region?.Area.Any(rect =>
        from.X >= rect.Start.X - range && from.Y >= rect.Start.Y - range && from.X < rect.End.X + range && from.Y < rect.End.Y + range)
      == true;

    public virtual int GetNewVendorSystemMaxVendors() =>
      (int)(GetAosEntry()?.Vendors ?? 0 * BonusStorageScalar);

    public virtual bool CanPlaceNewVendor() =>
      !IsAosRules || (!NewVendorSystem ? CheckAosLockdowns(10) :
        PlayerVendors.Count + VendorRentalContracts.Count < GetNewVendorSystemMaxVendors());

    public virtual bool CanPlaceNewBarkeep() => PlayerBarkeepers.Count < MaximumBarkeepCount;

    public static void IsThereVendor(Point3D location, Map map, out bool vendor, out bool rentalContract)
    {
      vendor = false;
      rentalContract = false;

      IPooledEnumerable<IEntity> eable = map.GetObjectsInRange(location, 0);

      foreach (IEntity entity in eable)
        if (Math.Abs(location.Z - entity.Z) <= 16)
        {
          if (entity is PlayerVendor || entity is PlayerBarkeeper || entity is PlayerVendorPlaceholder)
          {
            vendor = true;
            break;
          }

          if (entity is VendorRentalContract)
          {
            rentalContract = true;
            break;
          }
        }

      eable.Free();
    }

    public List<Mobile> AvailableVendorsFor(Mobile m) =>
      PlayerVendors.Where(vendor => vendor.CanInteractWith(m, false)).Cast<Mobile>().ToList();

    public bool AreThereAvailableVendorsFor(Mobile m) =>
      PlayerVendors.Any(vendor => vendor.CanInteractWith(m, false));

    public void MoveAllToCrate()
    {
      RelocatedEntities.Clear();

      MovingCrate?.Hide();

      if (m_Trash != null)
      {
        m_Trash.Delete();
        m_Trash = null;
      }

      foreach (Item item in LockDowns)
        if (!item.Deleted)
        {
          item.IsLockedDown = false;
          item.IsSecure = false;
          item.Movable = true;

          if (item.Parent == null)
            DropToMovingCrate(item);
        }

      LockDowns.Clear();

      foreach (VendorRentalContract item in VendorRentalContracts)
        if (!item.Deleted)
        {
          item.IsLockedDown = false;
          item.IsSecure = false;
          item.Movable = true;

          if (item.Parent == null)
            DropToMovingCrate(item);
        }

      VendorRentalContracts.Clear();

      foreach (SecureInfo info in Secures)
      {
        Item item = info.Item;

        if (!item.Deleted)
        {
          if (item is StrongBox box)
            item = box.ConvertToStandardContainer();

          item.IsLockedDown = false;
          item.IsSecure = false;
          item.Movable = true;

          if (item.Parent == null)
            DropToMovingCrate(item);
        }
      }

      Secures.Clear();

      foreach (Item addon in Addons)
        if (!addon.Deleted)
        {
          Item deed = null;
          bool retainDeedHue = false; // if the items aren't hued but the deed itself is
          int hue = 0;

          BaseAddon ba = addon as BaseAddon;

          if (addon is IAddon baseAddon)
          {
            deed = baseAddon.Deed;

            // There are things that are IAddon which aren't BaseAddon
            if (ba?.RetainDeedHue == true)
            {
              retainDeedHue = true;

              for (int i = 0; hue == 0 && i < ba.Components.Count; ++i)
              {
                AddonComponent c = ba.Components[i];

                if (c.Hue != 0)
                  hue = c.Hue;
              }
            }
          }

          if (deed != null)
          {
            #region Mondain's Legacy

            if (deed is BaseAddonContainerDeed containerDeed && addon is BaseAddonContainer c)
            {
              c.DropItemsToGround();
              containerDeed.Resource = c.Resource;
            }
            else if (deed is BaseAddonDeed addonDeed && ba != null)
            {
              addonDeed.Resource = ba.Resource;
            }

            #endregion

            addon.Delete();

            if (retainDeedHue)
              deed.Hue = hue;

            DropToMovingCrate(deed);
          }
          else
          {
            DropToMovingCrate(addon);
          }
        }

      Addons.Clear();

      foreach (PlayerVendor mobile in PlayerVendors)
      {
        mobile.Return();
        mobile.Internalize();
        InternalizedVendors.Add(mobile);
      }

      foreach (PlayerBarkeeper mobile in PlayerBarkeepers)
      {
        mobile.Internalize();
        InternalizedVendors.Add(mobile);
      }
    }

    public List<IEntity> GetHouseEntities()
    {
      List<IEntity> list = new List<IEntity>();

      MovingCrate?.Hide();

      if (m_Trash != null && m_Trash.Map != Map.Internal)
        list.Add(m_Trash);

      list.AddRange(LockDowns.Where(item => item.Parent == null && item.Map != Map.Internal));
      list.AddRange(VendorRentalContracts.Cast<Item>().Where(item => item.Parent == null && item.Map != Map.Internal));
      list.AddRange(Secures.Select(info => info.Item).Where(item => item.Parent == null && item.Map != Map.Internal));
      list.AddRange(Addons.Where(item => item.Parent == null && item.Map != Map.Internal));

      foreach (PlayerVendor mobile in PlayerVendors)
      {
        mobile.Return();

        if (mobile.Map != Map.Internal)
          list.Add(mobile);
      }

      list.AddRange(PlayerBarkeepers.Cast<Mobile>().Where(mobile => mobile.Map != Map.Internal));

      return list;
    }

    public void RelocateEntities()
    {
      foreach (IEntity entity in GetHouseEntities())
      {
        Point3D relLoc = new Point3D(entity.X - X, entity.Y - Y, entity.Z - Z);
        RelocatedEntity relocEntity = new RelocatedEntity(entity, relLoc);

        RelocatedEntities.Add(relocEntity);

        if (entity is Item item)
          item.Internalize();
        else if (entity is Mobile mobile)
          mobile.Internalize();
      }
    }

    public void RestoreRelocatedEntities()
    {
      foreach (RelocatedEntity relocEntity in RelocatedEntities)
      {
        Point3D relLoc = relocEntity.RelativeLocation;
        Point3D location = new Point3D(relLoc.X + X, relLoc.Y + Y, relLoc.Z + Z);

        IEntity entity = relocEntity.Entity;
        if (entity is Item item)
        {
          if (!item.Deleted)
          {
            IAddon addon = item as IAddon;
            if (addon != null)
            {
              if (addon.CouldFit(location, Map))
              {
                item.MoveToWorld(location, Map);
                continue;
              }
            }
            else
            {
              int height;
              bool requireSurface;
              if (item is VendorRentalContract)
              {
                height = 16;
                requireSurface = true;
              }
              else
              {
                height = item.ItemData.Height;
                requireSurface = false;
              }

              if (Map.CanFit(location.X, location.Y, location.Z, height, false, false, requireSurface))
              {
                item.MoveToWorld(location, Map);
                continue;
              }
            }

            // The item can't fit

            if (item is TrashBarrel)
              item.Delete(); // Trash barrels don't go to the moving crate
            else
            {
              SetLockdown(item, false);
              item.IsSecure = false;
              item.Movable = true;

              Item relocateItem = item;

              if (item is StrongBox box)
                relocateItem = box.ConvertToStandardContainer();

              if (addon != null)
              {
                Item deed = addon.Deed;
                bool retainDeedHue = false; //if the items aren't hued but the deed itself is
                int hue = 0;

                if (item is BaseAddon ba && ba.RetainDeedHue
                ) //There are things that are IAddon which aren't BaseAddon
                {
                  retainDeedHue = true;

                  for (int i = 0; hue == 0 && i < ba.Components.Count; ++i)
                  {
                    AddonComponent c = ba.Components[i];

                    if (c.Hue != 0)
                      hue = c.Hue;
                  }
                }

                #region Mondain's Legacy

                if (deed != null)
                {
                  if (deed is BaseAddonContainerDeed containerDeed && item is BaseAddonContainer c)
                  {
                    c.DropItemsToGround();

                    containerDeed.Resource = c.Resource;
                  }
                  else if (deed is BaseAddonDeed addonDeed && item is BaseAddon baseAddon)
                    addonDeed.Resource = baseAddon.Resource;

                  if (retainDeedHue)
                    deed.Hue = hue;
                }

                #endregion

                relocateItem = deed;
                item.Delete();
              }

              if (relocateItem != null)
                DropToMovingCrate(relocateItem);
            }
          }

          if (m_Trash == item)
            m_Trash = null;

          LockDowns.Remove(item);
          if (item is VendorRentalContract contract)
            VendorRentalContracts.Remove(contract);
          Addons.Remove(item);
          for (int i = Secures.Count - 1; i >= 0; i--)
            if (Secures[i].Item == item)
              Secures.RemoveAt(i);
        }
        else if (entity is Mobile mobile && !mobile.Deleted)
        {
          if (Map.CanFit(location, 16, false, false))
            mobile.MoveToWorld(location, Map);
          else
            InternalizedVendors.Add(mobile);
        }
      }

      RelocatedEntities.Clear();
    }

    public void DropToMovingCrate(Item item)
    {
      MovingCrate ??= new MovingCrate(this);
      MovingCrate.DropItem(item);
    }

    public List<Item> GetItems()
    {
      if (Map == null || Map == Map.Internal)
        return new List<Item>();

      Point2D start = new Point2D(X + Components.Min.X, Y + Components.Min.Y);
      Point2D end = new Point2D(X + Components.Max.X + 1, Y + Components.Max.Y + 1);
      Rectangle2D rect = new Rectangle2D(start, end);

      IPooledEnumerable<Item> eable = Map.GetItemsInBounds(rect);
      List<Item> list = eable.Where(item => item.Movable && IsInside(item)).ToList();

      eable.Free();

      return list;
    }

    public List<Mobile> GetMobiles() =>
      Map == null || Map == Map.Internal ? new List<Mobile>()
        : Region.GetMobiles().Where(IsInside).ToList();

    public virtual bool CheckAosLockdowns(int need) => GetAosCurLockdowns() + need <= GetAosMaxLockdowns();

    public virtual bool CheckAosStorage(int need) =>
      GetAosCurSecures(out int fromSecures, out int fromVendors, out int fromLockdowns, out int fromMovingCrate) + need <=
      GetAosMaxSecures();

    public static void Configure()
    {
      LockedDownFlag = 1;
      SecureFlag = 2;

      Timer.DelayCall(TimeSpan.FromMinutes(1.0), TimeSpan.FromMinutes(1.0), Decay_OnTick);
    }

    public virtual int GetAosCurLockdowns() => GetLockdowns() + Secures?.Count ?? 0 + (NewVendorSystem ? 0 : PlayerVendors.Count * 10);

    public static bool CheckLockedDown(Item item) => FindHouseAt(item)?.HasLockedDownItem(item) == true;

    public static bool CheckSecured(Item item) => FindHouseAt(item)?.HasSecureItem(item) == true;

    public static bool CheckLockedDownOrSecured(Item item)
    {
      BaseHouse house = FindHouseAt(item);
      return house != null && (house.HasSecureItem(item) || house.HasLockedDownItem(item));
    }

    public static List<BaseHouse> GetHouses(Mobile m)
    {
      List<BaseHouse> list = new List<BaseHouse>();

      if (m != null && m_Table.TryGetValue(m, out List<BaseHouse> exists))
        list.AddRange(exists.Where(house => house?.Deleted == false && house.Owner == m));

      return list;
    }

    public static bool CheckHold(Mobile m, Container cont, Item item, bool message, bool checkItems, int plusItems,
      int plusWeight)
    {
      BaseHouse house = FindHouseAt(cont);

      if (house == null || !house.IsAosRules)
        return true;

      if (house.HasSecureItem(cont) && !house.CheckAosStorage(1 + item.TotalItems + plusItems))
      {
        if (message)
          m.SendLocalizedMessage(1061839); // This action would exceed the secure storage limit of the house.

        return false;
      }

      return true;
    }

    public static bool CheckAccessible(Mobile m, Item item)
    {
      if (m.AccessLevel >= AccessLevel.GameMaster)
        return true; // Staff can access anything

      BaseHouse house = FindHouseAt(item);

      return house == null || house.CheckSecureAccess(m, item) switch
      {
        SecureAccessResult.Accessible => true,
        SecureAccessResult.Inaccessible => false,
        _ => (!house.HasLockedDownItem(item) || house.IsCoOwner(m) && item is Container)
      };
    }

    public static BaseHouse FindHouseAt(Mobile m) => m?.Deleted != false ? null : FindHouseAt(m.Location, m.Map, 16);

    public static BaseHouse FindHouseAt(Item item) =>
      item?.Deleted != false ? null :
        FindHouseAt(item.GetWorldLocation(), item.Map, item.ItemData.Height);

    public static BaseHouse FindHouseAt(Point3D loc, Map map, int height)
    {
      if (map == null || map == Map.Internal)
        return null;

      Sector sector = map.GetSector(loc);

      for (int i = 0; i < sector.Multis.Count; ++i)
        if (sector.Multis[i] is BaseHouse house && house.IsInside(loc, height))
          return house;

      return null;
    }

    public bool IsInside(Mobile m) => m?.Deleted == false && m.Map == Map && IsInside(m.Location, 16);

    public bool IsInside(Item item) => item?.Deleted == false && item.Map == Map && IsInside(item.Location, item.ItemData.Height);

    public bool CheckAccessibility(Item item, Mobile from)
    {
      SecureAccessResult res = CheckSecureAccess(from, item);

      switch (res)
      {
        // case SecureAccessResult.Insecure: break;
        case SecureAccessResult.Accessible: return true;
        case SecureAccessResult.Inaccessible: return false;
      }

      if (!HasLockedDownItem(item))
        return true;
      if (from.AccessLevel >= AccessLevel.GameMaster)
        return true;
      if (item is Runebook)
        return true;
      if (item is ISecurable securable)
        return HasSecureAccess(from, securable.Level);
      if (item is Container)
        return IsCoOwner(from);
      if (item.Stackable)
        return true;

      return item switch
      {
        BaseLight _ => IsFriend(from),
        PotionKeg _ => IsFriend(from),
        Dices _ => true,
        RecallRune _ => true,
        TreasureMap _ => true,
        Clock _ => true,
        BaseInstrument _ => true,
        Dyes _ => true,
        VendorRentalContract _ => true,
        RewardBrazier _ => true,
        _ => false
      };
    }

    public virtual bool IsInside(Point3D p, int height)
    {
      if (Deleted)
        return false;

      MultiComponentList mcl = Components;

      int x = p.X - (X + mcl.Min.X);
      int y = p.Y - (Y + mcl.Min.Y);

      if (x < 0 || x >= mcl.Width || y < 0 || y >= mcl.Height)
        return false;

      if (this is HouseFoundation && y < mcl.Height - 1 && p.Z >= Z)
        return true;

      StaticTile[] tiles = mcl.Tiles[x][y];

      for (int j = 0; j < tiles.Length; ++j)
      {
        StaticTile tile = tiles[j];
        int id = tile.ID & TileData.MaxItemValue;
        ItemData data = TileData.ItemTable[id];

        // Slanted roofs do not count; they overhang blocking south and east sides of the multi
        if ((data.Flags & TileFlag.Roof) != 0)
          continue;

        // Signs and signposts are not considered part of the multi
        if (id >= 0xB95 && id <= 0xC0E || id >= 0xC43 && id <= 0xC44)
          continue;

        int tileZ = tile.Z + Z;

        if (p.Z == tileZ || p.Z + height > tileZ)
          return true;
      }

      return false;
    }

    public SecureAccessResult CheckSecureAccess(Mobile m, Item item)
    {
      if (Secures == null || !(item is Container))
        return SecureAccessResult.Insecure;

      for (int i = 0; i < Secures.Count; ++i)
      {
        SecureInfo info = Secures[i];

        if (info.Item == item)
          return HasSecureAccess(m, info.Level) ? SecureAccessResult.Accessible : SecureAccessResult.Inaccessible;
      }

      return SecureAccessResult.Insecure;
    }

    public override void OnMapChange()
    {
      if (LockDowns == null)
        return;

      UpdateRegion();

      if (Sign?.Deleted == false)
        Sign.Map = Map;

      if (Doors != null)
        foreach (BaseDoor item in Doors)
          item.Map = Map;

      foreach (IEntity entity in GetHouseEntities())
        if (entity is Item item)
          item.Map = Map;
        else if (entity is Mobile mobile)
          mobile.Map = Map;
    }

    public virtual void ChangeSignType(int itemID)
    {
      if (Sign != null)
        Sign.ItemID = itemID;
    }

    public virtual void UpdateRegion()
    {
      m_Region?.Unregister();

      if (Map != null)
      {
        m_Region = new HouseRegion(this);
        m_Region.Register();
      }
      else
      {
        m_Region = null;
      }
    }

    public override void OnLocationChange(Point3D oldLocation)
    {
      if (LockDowns == null)
        return;

      int x = base.Location.X - oldLocation.X;
      int y = base.Location.Y - oldLocation.Y;
      int z = base.Location.Z - oldLocation.Z;

      if (Sign?.Deleted == false)
        Sign.Location = new Point3D(Sign.X + x, Sign.Y + y, Sign.Z + z);

      UpdateRegion();

      if (Doors != null)
        foreach (BaseDoor item in Doors)
          if (!item.Deleted)
            item.Location = new Point3D(item.X + x, item.Y + y, item.Z + z);

      foreach (IEntity entity in GetHouseEntities())
      {
        Point3D newLocation = new Point3D(entity.X + x, entity.Y + y, entity.Z + z);

        if (entity is Item item)
          item.Location = newLocation;
        else if (entity is Mobile mobile)
          mobile.Location = newLocation;
      }
    }

    public BaseDoor AddEastDoor(int x, int y, int z) => AddEastDoor(true, x, y, z);

    public BaseDoor AddEastDoor(bool wood, int x, int y, int z)
    {
      BaseDoor door = MakeDoor(wood, DoorFacing.SouthCW);

      AddDoor(door, x, y, z);

      return door;
    }

    public BaseDoor AddSouthDoor(int x, int y, int z) => AddSouthDoor(true, x, y, z);

    public BaseDoor AddSouthDoor(bool wood, int x, int y, int z)
    {
      BaseDoor door = MakeDoor(wood, DoorFacing.WestCW);

      AddDoor(door, x, y, z);

      return door;
    }

    public BaseDoor AddEastDoor(int x, int y, int z, uint k) => AddEastDoor(true, x, y, z, k);

    public BaseDoor AddEastDoor(bool wood, int x, int y, int z, uint k)
    {
      BaseDoor door = MakeDoor(wood, DoorFacing.SouthCW);

      door.Locked = true;
      door.KeyValue = k;

      AddDoor(door, x, y, z);

      return door;
    }

    public BaseDoor AddSouthDoor(int x, int y, int z, uint k) => AddSouthDoor(true, x, y, z, k);

    public BaseDoor AddSouthDoor(bool wood, int x, int y, int z, uint k)
    {
      BaseDoor door = MakeDoor(wood, DoorFacing.WestCW);

      door.Locked = true;
      door.KeyValue = k;

      AddDoor(door, x, y, z);

      return door;
    }

    public BaseDoor[] AddSouthDoors(int x, int y, int z, uint k) => AddSouthDoors(true, x, y, z, k);

    public BaseDoor[] AddSouthDoors(bool wood, int x, int y, int z, uint k)
    {
      BaseDoor westDoor = MakeDoor(wood, DoorFacing.WestCW);
      BaseDoor eastDoor = MakeDoor(wood, DoorFacing.EastCCW);

      westDoor.Locked = true;
      eastDoor.Locked = true;

      westDoor.KeyValue = k;
      eastDoor.KeyValue = k;

      westDoor.Link = eastDoor;
      eastDoor.Link = westDoor;

      AddDoor(westDoor, x, y, z);
      AddDoor(eastDoor, x + 1, y, z);

      return new[] { westDoor, eastDoor };
    }

    public uint CreateKeys(Mobile m)
    {
      uint value = Key.RandomValue();

      if (!IsAosRules)
      {
        Key packKey = new Key(KeyType.Gold);
        Key bankKey = new Key(KeyType.Gold);

        packKey.KeyValue = value;
        bankKey.KeyValue = value;

        packKey.LootType = LootType.Newbied;
        bankKey.LootType = LootType.Newbied;

        BankBox box = m.BankBox;

        if (!box.TryDropItem(m, bankKey, false))
          bankKey.Delete();

        m.AddToBackpack(packKey);
      }

      return value;
    }

    public BaseDoor[] AddSouthDoors(int x, int y, int z) => AddSouthDoors(true, x, y, z, false);

    public BaseDoor[] AddSouthDoors(bool wood, int x, int y, int z, bool inv)
    {
      BaseDoor westDoor = MakeDoor(wood, inv ? DoorFacing.WestCCW : DoorFacing.WestCW);
      BaseDoor eastDoor = MakeDoor(wood, inv ? DoorFacing.EastCW : DoorFacing.EastCCW);

      westDoor.Link = eastDoor;
      eastDoor.Link = westDoor;

      AddDoor(westDoor, x, y, z);
      AddDoor(eastDoor, x + 1, y, z);

      return new[] { westDoor, eastDoor };
    }

    public BaseDoor MakeDoor(bool wood, DoorFacing facing)
    {
      if (wood)
        return new DarkWoodHouseDoor(facing);
      return new MetalHouseDoor(facing);
    }

    public void AddDoor(BaseDoor door, int xoff, int yoff, int zoff)
    {
      door.MoveToWorld(new Point3D(xoff + X, yoff + Y, zoff + Z), Map);
      Doors.Add(door);
    }

    public void AddTrashBarrel(Mobile from)
    {
      if (!IsActive)
        return;

      for (int i = 0; Doors != null && i < Doors.Count; ++i)
      {
        BaseDoor door = Doors[i];
        Point3D p = door.Location;

        if (door.Open)
          p = new Point3D(p.X - door.Offset.X, p.Y - door.Offset.Y, p.Z - door.Offset.Z);

        if (from.Z + 16 >= p.Z && p.Z + 16 >= from.Z)
          if (from.InRange(p, 1))
          {
            from.SendLocalizedMessage(502120); // You cannot place a trash barrel near a door or near steps.
            return;
          }
      }

      if (m_Trash?.Deleted != false)
      {
        m_Trash = new TrashBarrel { Movable = false };
        m_Trash.MoveToWorld(from.Location, from.Map);

        from.SendLocalizedMessage(502121);
        /* You have a new trash barrel.
         * Three minutes after you put something in the barrel, the trash will be emptied.
         * Be forewarned, this is permanent!
         */
      }
      else
      {
        from.SendLocalizedMessage(502117); // You already have a trash barrel!
      }
    }

    public void SetSign(int xoff, int yoff, int zoff)
    {
      Sign = new HouseSign(this);
      Sign.MoveToWorld(new Point3D(X + xoff, Y + yoff, Z + zoff), Map);
    }

    private void SetLockdown(Item i, bool locked, bool checkContains = false)
    {
      if (LockDowns == null)
        return;

      if (i is BaseAddonContainer)
        i.Movable = false;
      else
        i.Movable = !locked;

      i.IsLockedDown = locked;

      if (locked)
      {
        if (i is VendorRentalContract contract)
        {
          if (!VendorRentalContracts.Contains(contract))
            VendorRentalContracts.Add(contract);
        }
        else
        {
          if (!checkContains || !LockDowns.Contains(i))
            LockDowns.Add(i);
        }
      }
      else
      {
        if (i is VendorRentalContract contract)
          VendorRentalContracts.Remove(contract);
        LockDowns.Remove(i);
      }

      if (!locked)
        i.SetLastMoved();

      if (i is Container && (!locked || !(i is BaseBoard || i is Aquarium || i is FishBowl)))
        foreach (Item c in i.Items)
          SetLockdown(c, locked, checkContains);
    }

    public bool LockDown(Mobile m, Item item) => LockDown(m, item, true);

    public bool LockDown(Mobile m, Item item, bool checkIsInside)
    {
      if (!IsCoOwner(m) || !IsActive)
        return false;

      if (item is BaseAddonContainer || item.Movable && !HasSecureItem(item))
      {
        int amt = 1 + item.TotalItems;

        Item rootItem = item.RootParent as Item;
        Item parentItem = item.Parent as Item;

        if (checkIsInside && item.RootParent is Mobile)
          m.SendLocalizedMessage(1005525); //That is not in your house
        else if (checkIsInside && !IsInside(item.GetWorldLocation(), item.ItemData.Height))
          m.SendLocalizedMessage(1005525); //That is not in your house
        else if (Ethic.IsImbued(item))
          m.SendLocalizedMessage(1005377); //You cannot lock that down
        else if (HasSecureItem(rootItem))
          m.SendLocalizedMessage(501737); // You need not lock down items in a secure container.
        else if (parentItem != null && !HasLockedDownItem(parentItem))
          m.SendLocalizedMessage(501736); // You must lockdown the container first!
        else if (!(item is VendorRentalContract) && (IsAosRules
                   ? !CheckAosLockdowns(amt) || !CheckAosStorage(amt)
                   : LockDownCount + amt > MaxLockDowns))
          m.SendLocalizedMessage(1005379); //That would exceed the maximum lock down limit for this house
        else
        {
          SetLockdown(item, true);
          return true;
        }
      }
      else if (LockDowns.IndexOf(item) != -1)
      {
        m.LocalOverheadMessage(MessageType.Regular, 0x3E9, 1005526); //That is already locked down
        return true;
      }
      else if (item is HouseSign || item is Static)
        m.LocalOverheadMessage(MessageType.Regular, 0x3E9, 1005526); // This is already locked down.
      else
        m.SendLocalizedMessage(1005377); //You cannot lock that down

      return false;
    }

    public bool CheckTransferPosition(Mobile from, Mobile to)
    {
      bool isValid = true;
      Item sign = Sign;
      Point3D p = sign?.GetWorldLocation() ?? Point3D.Zero;

      if (from.Map != Map || to.Map != Map)
        isValid = false;
      else if (sign == null)
        isValid = false;
      else if (from.Map != sign.Map || to.Map != sign.Map)
        isValid = false;
      else if (IsInside(from))
        isValid = false;
      else if (IsInside(to))
        isValid = false;
      else if (!from.InRange(p, 2))
        isValid = false;
      else if (!to.InRange(p, 2))
        isValid = false;

      if (!isValid)
        from.SendLocalizedMessage(
          1062067); // In order to transfer the house, you and the recipient must both be outside the building and within two paces of the house sign.

      return isValid;
    }

    public void BeginConfirmTransfer(Mobile from, Mobile to)
    {
      if (Deleted || !from.CheckAlive() || !IsOwner(from))
        return;

      if (NewVendorSystem && HasPersonalVendors)
        from.SendLocalizedMessage(
          1062467); // You cannot trade this house while you still have personal vendors inside.
      else if (DecayLevel == DecayLevel.DemolitionPending)
        from.SendLocalizedMessage(
          1005321); // This house has been marked for demolition, and it cannot be transferred.
      else if (from == to)
        from.SendLocalizedMessage(1005330); // You cannot transfer a house to yourself, silly.
      else if (to.Player)
      {
        if (HasAccountHouse(to))
          from.SendLocalizedMessage(501388); // You cannot transfer ownership to another house owner or co-owner!
        else if (CheckTransferPosition(from, to))
        {
          from.SendLocalizedMessage(1005326); // Please wait while the other player verifies the transfer.

          if (HasRentedVendors)
          {
            /* You are about to be traded a home that has active vendor contracts.
             * While there are active vendor contracts in this house, you
             * <strong>cannot</strong> demolish <strong>OR</strong> customize the home.
             * When you accept this house, you also accept landlordship for every
             * contract vendor in the house.
             */
            to.SendGump(
              new WarningGump(1060635, 30720, 1062487, 32512, 420, 280, okay => ConfirmTransfer_Callback(to, okay, from)));
          }
          else
          {
            to.CloseGump<HouseTransferGump>();
            to.SendGump(new HouseTransferGump(from, to, this));
          }
        }
      }
      else
      {
        from.SendLocalizedMessage(501384); // Only a player can own a house!
      }
    }

    private void ConfirmTransfer_Callback(Mobile to, bool ok, Mobile from)
    {
      if (!ok || Deleted || !from.CheckAlive() || !IsOwner(from))
        return;

      if (CheckTransferPosition(from, to))
      {
        to.CloseGump<HouseTransferGump>();
        to.SendGump(new HouseTransferGump(from, to, this));
      }
    }

    public void EndConfirmTransfer(Mobile from, Mobile to)
    {
      if (Deleted || !from.CheckAlive() || !IsOwner(from))
        return;

      if (NewVendorSystem && HasPersonalVendors)
        from.SendLocalizedMessage(
          1062467); // You cannot trade this house while you still have personal vendors inside.
      else if (DecayLevel == DecayLevel.DemolitionPending)
        from.SendLocalizedMessage(
          1005321); // This house has been marked for demolition, and it cannot be transferred.
      else if (from == to)
        from.SendLocalizedMessage(1005330); // You cannot transfer a house to yourself, silly.
      else if (to.Player)
      {
        if (HasAccountHouse(to))
          from.SendLocalizedMessage(501388); // You cannot transfer ownership to another house owner or co-owner!
        else if (CheckTransferPosition(from, to))
        {
          NetState fromState = from.NetState, toState = to.NetState;

          if (fromState != null && toState != null)
          {
            if (from.HasTrade)
              from.SendLocalizedMessage(
                1062071); // You cannot trade a house while you have other trades pending.
            else if (to.HasTrade)
              to.SendLocalizedMessage(
                1062071); // You cannot trade a house while you have other trades pending.
            else if (!to.Alive)
              // TODO: Check if the message is correct.
              from.SendLocalizedMessage(1062069); // You cannot transfer this house to that person.
            else
            {
              Container c = fromState.AddTrade(toState);

              c.DropItem(new TransferItem(this));
            }
          }
        }
      }
      else
        from.SendLocalizedMessage(501384); // Only a player can own a house!
    }

    public void Release(Mobile m, Item item)
    {
      if (!IsCoOwner(m) || !IsActive)
        return;

      if (HasLockedDownItem(item))
      {
        item.PublicOverheadMessage(MessageType.Label, 0x3B2, 501657); //[no longer locked down]
        SetLockdown(item, false);
        //TidyItemList( m_LockDowns );

        (item as RewardBrazier)?.TurnOff();
      }
      else if (HasSecureItem(item))
        ReleaseSecure(m, item);
      else
        m.LocalOverheadMessage(MessageType.Regular, 0x3E9, 1010416); // This is not locked down or secured.
    }

    public void AddSecure(Mobile m, Item item)
    {
      if (Secures == null || !IsOwner(m) || !IsActive)
        return;

      if (!IsInside(item))
        m.SendLocalizedMessage(1005525); // That is not in your house
      else if (HasLockedDownItem(item))
        m.SendLocalizedMessage(1010550); // This is already locked down and cannot be secured.
      else if (!(item is Container))
        LockDown(m, item);
      else
      {
        SecureInfo info = null;

        for (int i = 0; info == null && i < Secures.Count; ++i)
          if (Secures[i].Item == item)
            info = Secures[i];

        if (info != null)
        {
          m.CloseGump<SetSecureLevelGump>();
          m.SendGump(new SetSecureLevelGump(m_Owner, info, this));
        }
        else if (item.Parent != null)
          m.SendLocalizedMessage(1010423); // You cannot secure this, place it on the ground first.
        // Mondain's Legacy mod
        else if (!(item is BaseAddonContainer) && !item.Movable)
          m.SendLocalizedMessage(1010424); // You cannot secure this.
        else if (!IsAosRules && SecureCount >= MaxSecures)
          // The maximum number of secure items has been reached :
          m.SendLocalizedMessage(1008142, true, MaxSecures.ToString());
        else if (IsAosRules ? !CheckAosLockdowns(1) : LockDownCount + 125 >= MaxLockDowns)
          m.SendLocalizedMessage(1005379); // That would exceed the maximum lock down limit for this house
        else if (IsAosRules && !CheckAosStorage(item.TotalItems))
          m.SendLocalizedMessage(1061839); // This action would exceed the secure storage limit of the house.
        else
        {
          info = new SecureInfo((Container)item, SecureLevel.Owner);

          item.IsLockedDown = false;
          item.IsSecure = true;

          Secures.Add(info);
          LockDowns.Remove(item);
          item.Movable = false;

          m.CloseGump<SetSecureLevelGump>();
          m.SendGump(new SetSecureLevelGump(m_Owner, info, this));
        }
      }
    }

    public virtual bool IsCombatRestricted(Mobile m)
    {
      if (m == null || !m.Player || m.AccessLevel >= AccessLevel.GameMaster || !IsAosRules ||
          m_Owner != null && m_Owner.AccessLevel >= AccessLevel.GameMaster)
        return false;

      for (int i = 0; i < m.Aggressed.Count; ++i)
      {
        AggressorInfo info = m.Aggressed[i];

        if (info.Defender.Player && info.Defender.Alive &&
            DateTime.UtcNow - info.LastCombatTime < HouseRegion.CombatHeatDelay &&
            (!(m.Guild is Guild attackerGuild) || !(info.Defender.Guild is Guild defenderGuild) ||
             defenderGuild != attackerGuild && !defenderGuild.IsEnemy(attackerGuild)))
          return true;
      }

      return false;
    }

    public bool HasSecureAccess(Mobile m, SecureLevel level) =>
      m.AccessLevel >= AccessLevel.GameMaster || !IsCombatRestricted(m) && level switch
      {
        SecureLevel.Owner => IsOwner(m),
        SecureLevel.CoOwners => IsCoOwner(m),
        SecureLevel.Friends => IsFriend(m),
        SecureLevel.Anyone => true,
        SecureLevel.Guild => IsGuildMember(m),
        _ => false
      };

    public void ReleaseSecure(Mobile m, Item item)
    {
      if (Secures == null || !IsOwner(m) || item is StrongBox || !IsActive)
        return;

      for (int i = 0; i < Secures.Count; ++i)
      {
        SecureInfo info = Secures[i];

        if (info.Item == item && HasSecureAccess(m, info.Level))
        {
          item.IsLockedDown = false;
          item.IsSecure = false;

          item.Movable = !(item is BaseAddonContainer);
          item.SetLastMoved();
          item.PublicOverheadMessage(MessageType.Label, 0x3B2, 501656); //[no longer secure]
          Secures.RemoveAt(i);
          return;
        }
      }

      m.SendLocalizedMessage(501717); //This isn't secure...
    }

    public void AddStrongBox(Mobile from)
    {
      if (!IsCoOwner(from) || !IsActive)
        return;

      if (from == Owner)
      {
        from.SendLocalizedMessage(502109); // Owners don't get a strong box
        return;
      }

      if (IsAosRules ? !CheckAosLockdowns(1) : LockDownCount + 1 > MaxLockDowns)
      {
        from.SendLocalizedMessage(1005379); //That would exceed the maximum lock down limit for this house
        return;
      }

      foreach (SecureInfo info in Secures)
      {
        Container c = info.Item;

        if (!c.Deleted && c is StrongBox box && box.Owner == from)
        {
          from.SendLocalizedMessage(502112); //You already have a strong box
          return;
        }
      }

      for (int i = 0; Doors != null && i < Doors.Count; ++i)
      {
        BaseDoor door = Doors[i];
        Point3D p = door.Location;

        if (door.Open)
          p = new Point3D(p.X - door.Offset.X, p.Y - door.Offset.Y, p.Z - door.Offset.Z);

        if (from.Z + 16 >= p.Z && p.Z + 16 >= from.Z)
          if (from.InRange(p, 1))
          {
            from.SendLocalizedMessage(502113); // You cannot place a strongbox near a door or near steps.
            return;
          }
      }

      StrongBox sb = new StrongBox(from, this) { Movable = false, IsLockedDown = false, IsSecure = true };
      Secures.Add(new SecureInfo(sb, SecureLevel.CoOwners));
      sb.MoveToWorld(from.Location, from.Map);
    }

    public void Kick(Mobile from, Mobile targ)
    {
      if (!IsFriend(from) || Friends == null)
        return;

      if (targ.AccessLevel > AccessLevel.Player && from.AccessLevel <= targ.AccessLevel)
        from.SendLocalizedMessage(501346); // Uh oh...a bigger boot may be required!
      else if (IsFriend(targ) && !Core.ML)
        from.SendLocalizedMessage(501348); // You cannot eject a friend of the house!
      else if (targ is PlayerVendor)
        from.SendLocalizedMessage(501351); // You cannot eject a vendor.
      else if (!IsInside(targ))
        from.SendLocalizedMessage(501352); // You may not eject someone who is not in your house!
      else if (targ is BaseCreature creature && creature.NoHouseRestrictions)
        from.SendLocalizedMessage(501347); // You cannot eject that from the house!
      else
      {
        targ.MoveToWorld(BanLocation, Map);

        from.SendLocalizedMessage(1042840, targ.Name); // ~1_PLAYER NAME~ has been ejected from this house.
        targ.SendLocalizedMessage(501341);
        /* You have been ejected from this house.
         * If you persist in entering, you may be banned from the house.
         */
      }
    }

    public void RemoveAccess(Mobile from, Mobile targ)
    {
      if (!IsFriend(from) || Access == null)
        return;

      if (Access.Contains(targ))
      {
        Access.Remove(targ);

        if (!HasAccess(targ) && IsInside(targ))
        {
          targ.Location = BanLocation;
          targ.SendLocalizedMessage(1060734); // Your access to this house has been revoked.
        }

        from.SendLocalizedMessage(1050051); // The invitation has been revoked.
      }
    }

    public void RemoveBan(Mobile from, Mobile targ)
    {
      if (!IsCoOwner(from) || Bans == null)
        return;

      if (Bans.Contains(targ))
      {
        Bans.Remove(targ);

        from.SendLocalizedMessage(501297); // The ban is lifted.
      }
    }

    public void Ban(Mobile from, Mobile targ)
    {
      if (!IsFriend(from) || Bans == null)
        return;

      if (targ.AccessLevel > AccessLevel.Player && from.AccessLevel <= targ.AccessLevel)
        from.SendLocalizedMessage(501354); // Uh oh...a bigger boot may be required.
      else if (IsFriend(targ))
        from.SendLocalizedMessage(501348); // You cannot eject a friend of the house!
      else if (targ is PlayerVendor)
        from.SendLocalizedMessage(501351); // You cannot eject a vendor.
      else if (Bans.Count >= MaxBans)
        from.SendLocalizedMessage(501355); // The ban limit for this house has been reached!
      else if (IsBanned(targ))
        from.SendLocalizedMessage(501356); // This person is already banned!
      else if (!IsInside(targ))
        from.SendLocalizedMessage(501352); // You may not eject someone who is not in your house!
      else if (!Public && IsAosRules)
        from.SendLocalizedMessage(
          1062521); // You cannot ban someone from a private house.  Revoke their access instead.
      else if (targ is BaseCreature bc && bc.NoHouseRestrictions)
        from.SendLocalizedMessage(1062040); // You cannot ban that.
      else
      {
        Bans.Add(targ);

        from.SendLocalizedMessage(1042839, targ.Name); // ~1_PLAYER_NAME~ has been banned from this house.
        targ.SendLocalizedMessage(501340); // You have been banned from this house.

        targ.MoveToWorld(BanLocation, Map);
      }
    }

    public void GrantAccess(Mobile from, Mobile targ)
    {
      if (!IsFriend(from) || Access == null)
        return;

      if (HasAccess(targ))
        from.SendLocalizedMessage(1060729); // That person already has access to this house.
      else if (!targ.Player)
        from.SendLocalizedMessage(1060712); // That is not a player.
      else if (IsBanned(targ))
        from.SendLocalizedMessage(501367); // This person is banned!  Unban them first.
      else
      {
        Access.Add(targ);

        targ.SendLocalizedMessage(1060735); // You have been granted access to this house.
      }
    }

    public void AddCoOwner(Mobile from, Mobile targ)
    {
      if (!IsOwner(from) || CoOwners == null || Friends == null)
        return;

      if (IsOwner(targ))
        from.SendLocalizedMessage(501360); // This person is already the house owner!
      else if (Friends.Contains(targ))
        from.SendLocalizedMessage(501361); // This person is a friend of the house. Remove them first.
      else if (!targ.Player)
        from.SendLocalizedMessage(501362); // That can't be a co-owner of the house.
      else if (!Core.AOS && HasAccountHouse(targ))
        from.SendLocalizedMessage(501364); // That person is already a house owner.
      else if (IsBanned(targ))
        from.SendLocalizedMessage(501367); // This person is banned!  Unban them first.
      else if (CoOwners.Count >= MaxCoOwners)
        from.SendLocalizedMessage(501368); // Your co-owner list is full!
      else if (CoOwners.Contains(targ))
        from.SendLocalizedMessage(501369); // This person is already on your co-owner list!
      else
      {
        CoOwners.Add(targ);

        targ.Delta(MobileDelta.Noto);
        targ.SendLocalizedMessage(501343); // You have been made a co-owner of this house.
      }
    }

    public void RemoveCoOwner(Mobile from, Mobile targ)
    {
      if (!IsOwner(from) || CoOwners == null)
        return;

      if (CoOwners.Contains(targ))
      {
        CoOwners.Remove(targ);

        targ.Delta(MobileDelta.Noto);

        from.SendLocalizedMessage(501299); // Co-owner removed from list.
        targ.SendLocalizedMessage(501300); // You have been removed as a house co-owner.

        foreach (SecureInfo info in Secures)
        {
          Container c = info.Item;

          if (c is StrongBox box && box.Owner == targ)
          {
            box.IsLockedDown = false;
            box.IsSecure = false;
            Secures.Remove(info);
            box.Destroy();
            break;
          }
        }
      }
    }

    public void AddFriend(Mobile from, Mobile targ)
    {
      if (!IsCoOwner(from) || Friends == null || CoOwners == null)
        return;

      if (IsOwner(targ))
        from.SendLocalizedMessage(501370); // This person is already an owner of the house!
      else if (CoOwners.Contains(targ))
        from.SendLocalizedMessage(501369); // This person is already on your co-owner list!
      else if (!targ.Player)
        from.SendLocalizedMessage(501371); // That can't be a friend of the house.
      else if (IsBanned(targ))
        from.SendLocalizedMessage(501374); // This person is banned!  Unban them first.
      else if (Friends.Count >= MaxFriends)
        from.SendLocalizedMessage(501375); // Your friends list is full!
      else if (Friends.Contains(targ))
        from.SendLocalizedMessage(501376); // This person is already on your friends list!
      else
      {
        Friends.Add(targ);

        targ.Delta(MobileDelta.Noto);
        targ.SendLocalizedMessage(501337); // You have been made a friend of this house.
      }
    }

    public void RemoveFriend(Mobile from, Mobile targ)
    {
      if (!IsCoOwner(from) || Friends == null)
        return;

      if (Friends.Contains(targ))
      {
        Friends.Remove(targ);

        targ.Delta(MobileDelta.Noto);

        from.SendLocalizedMessage(501298); // Friend removed from list.
        targ.SendLocalizedMessage(1060751); // You are no longer a friend of this house.
      }
    }

    public override void Serialize(GenericWriter writer)
    {
      base.Serialize(writer);

      writer.Write(15); // version

      if (!DynamicDecay.Enabled)
      {
        writer.Write(-1);
      }
      else
      {
        writer.Write((int)m_CurrentStage);
        writer.Write(NextDecayStage);
      }

      writer.Write(m_RelativeBanLocation);

      writer.WriteItemList(VendorRentalContracts, true);
      writer.WriteMobileList(InternalizedVendors, true);

      writer.WriteEncodedInt(RelocatedEntities.Count);
      foreach (RelocatedEntity relEntity in RelocatedEntities)
      {
        writer.Write(relEntity.RelativeLocation);

        if (relEntity.Entity.Deleted)
          writer.Write(Serial.MinusOne);
        else
          writer.Write(relEntity.Entity.Serial);
      }

      writer.WriteEncodedInt(VendorInventories.Count);
      for (int i = 0; i < VendorInventories.Count; i++)
      {
        VendorInventory inventory = VendorInventories[i];
        inventory.Serialize(writer);
      }

      writer.Write(LastRefreshed);
      writer.Write(RestrictDecay);

      writer.Write(Visits);

      writer.Write(Price);

      writer.WriteMobileList(Access);

      writer.Write(BuiltOn);
      writer.Write(LastTraded);

      writer.WriteItemList(Addons, true);

      writer.Write(Secures.Count);

      for (int i = 0; i < Secures.Count; ++i)
        Secures[i].Serialize(writer);

      writer.Write(m_Public);

      //writer.Write( BanLocation );

      writer.Write(m_Owner);

      // Version 5 no longer serializes region coords
      /*writer.Write( (int)m_Region.Coords.Count );
      foreach( Rectangle2D rect in m_Region.Coords )
      {
        writer.Write( rect );
      }*/

      writer.WriteMobileList(CoOwners, true);
      writer.WriteMobileList(Friends, true);
      writer.WriteMobileList(Bans, true);

      writer.Write(Sign);
      writer.Write(m_Trash);

      writer.WriteItemList(Doors, true);
      writer.WriteItemList(LockDowns, true);
      //writer.WriteItemList( m_Secures, true );

      writer.Write(MaxLockDowns);
      writer.Write(MaxSecures);

      // Items in locked down containers that aren't locked down themselves must decay!
      for (int i = 0; i < LockDowns.Count; ++i)
      {
        Item item = LockDowns[i];

        if (item is Container cont && !(cont is BaseBoard || cont is Aquarium || cont is FishBowl))
        {
          List<Item> children = cont.Items;

          for (int j = 0; j < children.Count; ++j)
          {
            Item child = children[j];

            if (child.Decays && !child.IsLockedDown && !child.IsSecure &&
                child.LastMoved + child.DecayTime <= DateTime.UtcNow)
              Timer.DelayCall(TimeSpan.Zero, child.Delete);
          }
        }
      }
    }

    public override void Deserialize(GenericReader reader)
    {
      base.Deserialize(reader);

      int version = reader.ReadInt();
      int count;
      bool loadedDynamicDecay = false;

      switch (version)
      {
        case 15:
        {
          int stage = reader.ReadInt();

          if (stage != -1)
          {
            m_CurrentStage = (DecayLevel)stage;
            NextDecayStage = reader.ReadDateTime();
            loadedDynamicDecay = true;
          }

          goto case 14;
        }
        case 14:
        {
          m_RelativeBanLocation = reader.ReadPoint3D();
          goto case 13;
        }
        case 13: // removed ban location serialization
        case 12:
        {
          VendorRentalContracts = reader.ReadStrongItemList<VendorRentalContract>();
          InternalizedVendors = reader.ReadStrongMobileList();

          int relocatedCount = reader.ReadEncodedInt();
          for (int i = 0; i < relocatedCount; i++)
          {
            Point3D relLocation = reader.ReadPoint3D();
            IEntity entity = World.FindEntity(reader.ReadUInt());

            if (entity != null)
              RelocatedEntities.Add(new RelocatedEntity(entity, relLocation));
          }

          int inventoryCount = reader.ReadEncodedInt();
          for (int i = 0; i < inventoryCount; i++)
          {
            VendorInventory inventory = new VendorInventory(this, reader);
            VendorInventories.Add(inventory);
          }

          goto case 11;
        }
        case 11:
        {
          LastRefreshed = reader.ReadDateTime();
          RestrictDecay = reader.ReadBool();
          goto case 10;
        }
        case 10: // just a signal for updates
        case 9:
        {
          Visits = reader.ReadInt();
          goto case 8;
        }
        case 8:
        {
          Price = reader.ReadInt();
          goto case 7;
        }
        case 7:
        {
          Access = reader.ReadStrongMobileList();
          goto case 6;
        }
        case 6:
        {
          BuiltOn = reader.ReadDateTime();
          LastTraded = reader.ReadDateTime();
          goto case 5;
        }
        case 5: // just removed fields
        case 4:
        {
          Addons = reader.ReadStrongItemList();
          goto case 3;
        }
        case 3:
        {
          count = reader.ReadInt();
          Secures = new List<SecureInfo>(count);

          for (int i = 0; i < count; ++i)
          {
            SecureInfo info = new SecureInfo(reader);

            if (info.Item != null)
            {
              info.Item.IsSecure = true;
              Secures.Add(info);
            }
          }

          goto case 2;
        }
        case 2:
        {
          m_Public = reader.ReadBool();
          goto case 1;
        }
        case 1:
        {
          if (version < 13)
            reader.ReadPoint3D(); // house ban location
          goto case 0;
        }
        case 0:
        {
          if (version < 14)
            m_RelativeBanLocation = BaseBanLocation;

          if (version < 12)
          {
            VendorRentalContracts = new List<VendorRentalContract>();
            InternalizedVendors = new List<Mobile>();
          }

          if (version < 4)
            Addons = new List<Item>();

          if (version < 7)
            Access = new List<Mobile>();

          if (version < 8)
            Price = DefaultPrice;

          m_Owner = reader.ReadMobile();

          if (version < 5)
          {
            count = reader.ReadInt();

            for (int i = 0; i < count; i++)
              reader.ReadRect2D();
          }

          UpdateRegion();

          CoOwners = reader.ReadStrongMobileList();
          Friends = reader.ReadStrongMobileList();
          Bans = reader.ReadStrongMobileList();

          Sign = reader.ReadItem() as HouseSign;
          m_Trash = reader.ReadItem() as TrashBarrel;

          Doors = reader.ReadStrongItemList<BaseDoor>();
          LockDowns = reader.ReadStrongItemList();

          for (int i = 0; i < LockDowns.Count; ++i)
            LockDowns[i].IsLockedDown = true;

          for (int i = 0; i < VendorRentalContracts.Count; ++i)
            VendorRentalContracts[i].IsLockedDown = true;

          if (version < 3)
          {
            List<Item> items = reader.ReadStrongItemList();
            Secures = new List<SecureInfo>(items.Count);

            for (int i = 0; i < items.Count; ++i)
              if (items[i] is Container c)
              {
                c.IsSecure = true;
                Secures.Add(new SecureInfo(c, SecureLevel.CoOwners));
              }
          }

          MaxLockDowns = reader.ReadInt();
          MaxSecures = reader.ReadInt();

          if ((Map == null || Map == Map.Internal) && Location == Point3D.Zero)
            Delete();

          if (m_Owner != null)
          {
            if (!m_Table.TryGetValue(m_Owner, out List<BaseHouse> list))
              m_Table[m_Owner] = list = new List<BaseHouse>();

            list.Add(this);
          }

          break;
        }
      }

      if (version <= 1)
        ChangeSignType(0xBD2); //private house, plain brass sign

      if (version < 10) Timer.DelayCall(TimeSpan.Zero, FixLockdowns_Sandbox);

      if (version < 11)
        LastRefreshed = DateTime.UtcNow + TimeSpan.FromHours(24 * Utility.RandomDouble());

      if (DynamicDecay.Enabled && !loadedDynamicDecay)
      {
        DecayLevel old = GetOldDecayLevel();

        if (old == DecayLevel.DemolitionPending)
          old = DecayLevel.Collapsed;

        SetDynamicDecay(old);
      }

      if (!CheckDecay())
      {
        if (RelocatedEntities.Count > 0)
          Timer.DelayCall(TimeSpan.Zero, RestoreRelocatedEntities);

        if (m_Owner == null && Friends.Count == 0 && CoOwners.Count == 0)
          Timer.DelayCall(TimeSpan.FromSeconds(10.0), Delete);
      }
    }

    private void FixLockdowns_Sandbox()
    {
      List<Item> conts = LockDowns?.Where(item => item is Container).ToList();

      if (conts == null)
        return;

      foreach (Item cont in conts)
        SetLockdown(cont, true, true);
    }

    public static void HandleDeletion(Mobile mob)
    {
      List<BaseHouse> houses = GetHouses(mob);

      if (houses.Count == 0)
        return;

      Account acct = mob.Account as Account;
      Mobile trans = null;

      for (int i = 0; i < acct.Length; ++i)
        if (acct[i] != null && acct[i] != mob)
          trans = acct[i];

      for (int i = 0; i < houses.Count; ++i)
      {
        BaseHouse house = houses[i];

        bool canClaim = false;

        if (trans == null)
          canClaim = house.CoOwners.Count > 0;
        /*{
          for ( int j = 0; j < house.CoOwners.Count; ++j )
          {
            Mobile check = house.CoOwners[j] as Mobile;

            if ( check != null && !check.Deleted && !HasAccountHouse( check ) )
            {
              canClaim = true;
              break;
            }
          }
        }*/

        if (trans == null && !canClaim)
          Timer.DelayCall(TimeSpan.Zero, house.Delete);
        else
          house.Owner = trans;
      }
    }

    public int GetLockdowns()
    {
      int count = 0;

      if (LockDowns != null)
        for (int i = 0; i < LockDowns.Count; ++i)
        {
          if (LockDowns[i] != null)
          {
            Item item = LockDowns[i];

            if (!(item is Container))
              count += item.TotalItems;
          }

          count++;
        }

      return count;
    }

    public override void OnDelete()
    {
      RestoreRelocatedEntities();

      new FixColumnTimer(this).Start();

      base.OnDelete();
    }

    public override void OnAfterDelete()
    {
      base.OnAfterDelete();

      if (m_Owner != null)
      {
        if (!m_Table.TryGetValue(m_Owner, out List<BaseHouse> list))
          m_Table[m_Owner] = list = new List<BaseHouse>();

        list.Remove(this);
      }

      if (m_Region != null)
      {
        m_Region.Unregister();
        m_Region = null;
      }

      Sign?.Delete();

      m_Trash?.Delete();

      if (Doors != null)
      {
        for (int i = 0; i < Doors.Count; ++i)
        {
          Item item = Doors[i];

          item?.Delete();
        }

        Doors.Clear();
      }

      if (LockDowns != null)
      {
        for (int i = 0; i < LockDowns.Count; ++i)
        {
          Item item = LockDowns[i];

          if (item != null)
          {
            item.IsLockedDown = false;
            item.IsSecure = false;
            item.Movable = true;
            item.SetLastMoved();
          }
        }

        LockDowns.Clear();
      }

      if (VendorRentalContracts != null)
      {
        for (int i = 0; i < VendorRentalContracts.Count; ++i)
        {
          Item item = VendorRentalContracts[i];

          if (item != null)
          {
            item.IsLockedDown = false;
            item.IsSecure = false;
            item.Movable = true;
            item.SetLastMoved();
          }
        }

        VendorRentalContracts.Clear();
      }

      if (Secures != null)
      {
        for (int i = 0; i < Secures.Count; ++i)
        {
          SecureInfo info = Secures[i];

          if (info.Item is StrongBox)
          {
            info.Item.Destroy();
          }
          else
          {
            info.Item.IsLockedDown = false;
            info.Item.IsSecure = false;
            info.Item.Movable = true;
            info.Item.SetLastMoved();
          }
        }

        Secures.Clear();
      }

      if (Addons != null)
      {
        for (int i = 0; i < Addons.Count; ++i)
        {
          Item item = Addons[i];

          if (item != null)
          {
            if (!item.Deleted && item is IAddon addon)
            {
              Item deed = addon.Deed;
              bool retainDeedHue = false; //if the items aren't hued but the deed itself is
              int hue = 0;

              if (addon is BaseAddon ba && ba.RetainDeedHue
              ) //There are things that are IAddon which aren't BaseAddon
              {
                retainDeedHue = true;

                for (int j = 0; hue == 0 && j < ba.Components.Count; ++j)
                {
                  AddonComponent c = ba.Components[j];

                  if (c.Hue != 0)
                    hue = c.Hue;
                }
              }

              if (deed != null)
              {
                if (retainDeedHue)
                  deed.Hue = hue;
                deed.MoveToWorld(item.Location, item.Map);
              }
            }

            item.Delete();
          }
        }

        Addons.Clear();
      }

      foreach (VendorInventory inventory in VendorInventories.ToList())
        inventory.Delete();

      MovingCrate?.Delete();

      KillVendors();

      AllHouses.Remove(this);
    }

    public static bool HasHouse(Mobile m) => m != null && m_Table.TryGetValue(m, out List<BaseHouse> list) && list.Any(h => !h.Deleted);

    public static bool HasAccountHouse(Mobile m)
    {
      if (!(m.Account is Account a))
        return false;

      for (int i = 0; i < a.Length; ++i)
        if (a[i] != null && HasHouse(a[i]))
          return true;

      return false;
    }

    public bool IsOwner(Mobile m) =>
      m != null && (m == m_Owner || m.AccessLevel >= AccessLevel.GameMaster ||
        IsAosRules && AccountHandler.CheckAccount(m, m_Owner));

    public bool IsCoOwner(Mobile m) =>
      m != null && (IsOwner(m) || CoOwners?.Contains(m) == true ||
        !IsAosRules && AccountHandler.CheckAccount(m, m_Owner));

    public bool IsGuildMember(Mobile m) => Owner?.Guild != null && m?.Guild == Owner.Guild;

    public void RemoveKeys(Mobile m)
    {
      if (Doors != null)
      {
        uint keyValue = 0;

        for (int i = 0; keyValue == 0 && i < Doors.Count; ++i)
        {
          BaseDoor door = Doors[i];
          if (door != null)
            keyValue = door.KeyValue;
        }

        Key.RemoveKeys(m, keyValue);
      }
    }

    public void ChangeLocks(Mobile m)
    {
      uint keyValue = CreateKeys(m);

      if (Doors != null)
        for (int i = 0; i < Doors.Count; ++i)
        {
          BaseDoor door = Doors[i];
          if (door != null)
            door.KeyValue = keyValue;
        }
    }

    public void RemoveLocks()
    {
      if (Doors != null)
        for (int i = 0; i < Doors.Count; ++i)
        {
          BaseDoor door = Doors[i];
          if (door != null)
          {
            door.KeyValue = 0;
            door.Locked = false;
          }
        }
    }

    public virtual HouseDeed GetDeed() => null;

    public bool IsFriend(Mobile m)
    {
      if (m == null || Friends == null)
        return false;

      return IsCoOwner(m) || Friends.Contains(m);
    }

    public bool IsBanned(Mobile m)
    {
      if (m == null || m == Owner || m.AccessLevel > AccessLevel.Player || Bans == null)
        return false;

      Account theirAccount = m.Account as Account;

      for (int i = 0; i < Bans.Count; ++i)
      {
        Mobile c = Bans[i];

        if (c == m)
          return true;

        if (c.Account is Account bannedAccount && bannedAccount == theirAccount)
          return true;
      }

      return false;
    }

    public bool HasAccess(Mobile m)
    {
      if (m == null)
        return false;

      if (m.AccessLevel > AccessLevel.Player || IsFriend(m) || Access?.Contains(m) == true)
        return true;

      if (!(m is BaseCreature bc))
        return false;

      if (bc.NoHouseRestrictions)
        return true;

      if (!(bc.Controlled || bc.Summoned))
        return false;

      m = bc.ControlMaster ?? bc.SummonMaster;

      return m != null && (m.AccessLevel > AccessLevel.Player || IsFriend(m) || Access?.Contains(m) == true);
    }

    public bool HasLockedDownItem(Item check) =>
      LockDowns?.Contains(check) == true ||
      check is VendorRentalContract contract && VendorRentalContracts.Contains(contract);

    public bool HasSecureItem(Item item)
    {
      if (item == null)
        return false;

      for (int i = 0; i < Secures?.Count; ++i)
        if (Secures[i].Item == item)
          return true;

      return false;
    }

    public virtual Guildstone FindGuildstone()
    {
      Map map = Map;

      if (map == null)
        return null;

      MultiComponentList mcl = Components;
      IPooledEnumerable<Guildstone> eable =
        map.GetItemsInBounds<Guildstone>(new Rectangle2D(X + mcl.Min.X, Y + mcl.Min.Y, mcl.Width, mcl.Height));

      Guildstone item = eable.FirstOrDefault(Contains);
      eable.Free();
      return item;
    }

    private class TransferItem : Item
    {
      private BaseHouse m_House;

      public TransferItem(BaseHouse house) : base(0x14F0)
      {
        m_House = house;

        Hue = 0x480;
        Movable = false;
      }

      public TransferItem(Serial serial) : base(serial)
      {
      }

      public override string DefaultName => "a house transfer contract";

      public override void GetProperties(ObjectPropertyList list)
      {
        base.GetProperties(list);

        string houseName = m_House == null ? "an unnamed house" : m_House.Sign.GetName();
        string owner = m_House?.Owner?.Name ?? "nobody";

        int xLong = 0, yLat = 0, xMins = 0, yMins = 0;
        bool xEast = false, ySouth = false;

        bool valid = m_House != null && Sextant.Format(m_House.Location, m_House.Map, ref xLong, ref yLat, ref xMins,
                       ref yMins, ref xEast, ref ySouth);

        string location =
          valid ? $"{yLat}° {yMins}'{(ySouth ? "S" : "N")}, {xLong}° {xMins}'{(xEast ? "E" : "W")}" : "unknown";

        list.Add(1061112, Utility.FixHtml(houseName)); // House Name: ~1_val~
        list.Add(1061113, owner); // Owner: ~1_val~
        list.Add(1061114, location); // Location: ~1_val~
      }

      public override void Serialize(GenericWriter writer)
      {
        base.Serialize(writer);

        writer.Write(0); // version
      }

      public override void Deserialize(GenericReader reader)
      {
        base.Deserialize(reader);

        int version = reader.ReadInt();

        Delete();
      }

      public override bool AllowSecureTrade(Mobile from, Mobile to, Mobile newOwner, bool accepted)
      {
        if (!base.AllowSecureTrade(from, to, newOwner, accepted))
          return false;
        if (!accepted)
          return true;

        if (Deleted || m_House?.Deleted != false || !m_House.IsOwner(from) || !from.CheckAlive() ||
            !to.CheckAlive())
          return false;

        if (HasAccountHouse(to))
        {
          from.SendLocalizedMessage(501388); // You cannot transfer ownership to another house owner or co-owner!
          return false;
        }

        return m_House.CheckTransferPosition(from, to);
      }

      public override void OnSecureTrade(Mobile from, Mobile to, Mobile newOwner, bool accepted)
      {
        if (Deleted)
          return;

        Delete();

        if (m_House?.Deleted != false || !m_House.IsOwner(from) || !from.CheckAlive() || !to.CheckAlive())
          return;


        if (!accepted)
          return;

        from.SendLocalizedMessage(501338); // You have transferred ownership of the house.
        to.SendLocalizedMessage(501339);
        /* You are now the owner of this house.
         * The house's co-owner, friend, ban, and access lists have been cleared.
         * You should double-check the security settings on any doors and teleporters in the house.
         */

        m_House.RemoveKeys(from);
        m_House.Owner = to;
        m_House.Bans.Clear();
        m_House.Friends.Clear();
        m_House.CoOwners.Clear();
        m_House.ChangeLocks(to);
        m_House.LastTraded = DateTime.UtcNow;
      }
    }

    private class FixColumnTimer : Timer
    {
      private Map m_Map;
      private int m_StartX, m_StartY, m_EndX, m_EndY;

      public FixColumnTimer(BaseMulti multi) : base(TimeSpan.Zero)
      {
        m_Map = multi.Map;

        MultiComponentList mcl = multi.Components;

        m_StartX = multi.X + mcl.Min.X;
        m_StartY = multi.Y + mcl.Min.Y;
        m_EndX = multi.X + mcl.Max.X;
        m_EndY = multi.Y + mcl.Max.Y;
      }

      protected override void OnTick()
      {
        if (m_Map == null)
          return;

        for (int x = m_StartX; x <= m_EndX; ++x)
        for (int y = m_StartY; y <= m_EndY; ++y)
          m_Map.FixColumn(x, y);
      }
    }

    #region Dynamic decay system

    private DecayLevel m_CurrentStage;

    [CommandProperty(AccessLevel.GameMaster)]
    public DateTime NextDecayStage{ get; set; }

    public void ResetDynamicDecay()
    {
      m_CurrentStage = DecayLevel.Ageless;
      NextDecayStage = DateTime.MinValue;
    }

    public void SetDynamicDecay(DecayLevel level)
    {
      m_CurrentStage = level;

      if (DynamicDecay.Decays(level))
        NextDecayStage = DateTime.UtcNow + DynamicDecay.GetRandomDuration(level);
      else
        NextDecayStage = DateTime.MinValue;
    }

    #endregion
  }

  public enum DecayType
  {
    Ageless,
    AutoRefresh,
    ManualRefresh,
    Condemned
  }

  public enum DecayLevel
  {
    Ageless,
    LikeNew,
    Slightly,
    Somewhat,
    Fairly,
    Greatly,
    IDOC,
    Collapsed,
    DemolitionPending
  }

  public enum SecureAccessResult
  {
    Insecure,
    Accessible,
    Inaccessible
  }

  public enum SecureLevel
  {
    Owner,
    CoOwners,
    Friends,
    Anyone,
    Guild
  }

  public class SecureInfo : ISecurable
  {
    public SecureInfo(Container item, SecureLevel level)
    {
      Item = item;
      Level = level;
    }

    public SecureInfo(GenericReader reader)
    {
      Item = reader.ReadItem() as Container;
      Level = (SecureLevel)reader.ReadByte();
    }

    public Container Item{ get; }

    public SecureLevel Level{ get; set; }

    public void Serialize(GenericWriter writer)
    {
      writer.Write(Item);
      writer.Write((byte)Level);
    }
  }

  public class RelocatedEntity
  {
    public RelocatedEntity(IEntity entity, Point3D relativeLocation)
    {
      Entity = entity;
      RelativeLocation = relativeLocation;
    }

    public IEntity Entity{ get; }

    public Point3D RelativeLocation{ get; }
  }

  #region Targets

  public class LockdownTarget : Target
  {
    private BaseHouse m_House;
    private bool m_Release;

    public LockdownTarget(bool release, BaseHouse house) : base(12, false, TargetFlags.None)
    {
      CheckLOS = false;

      m_Release = release;
      m_House = house;
    }

    protected override void OnTargetNotAccessible(Mobile from, object targeted)
    {
      OnTarget(from, targeted);
    }

    protected override void OnTarget(Mobile from, object targeted)
    {
      if (!from.Alive || m_House.Deleted || !m_House.IsCoOwner(from))
        return;

      if (targeted is Item item)
      {
        if (m_Release)
        {
          if (item is AddonContainerComponent component)
          {
            if (component.Addon != null)
              m_House.Release(from, component.Addon);
          }
          else
          {
            m_House.Release(from, item);
          }
        }
        else
        {
          if (item is VendorRentalContract)
          {
            from.LocalOverheadMessage(MessageType.Regular, 0x3B2,
              1062392); // You must double click the contract in your pack to lock it down.
            from.LocalOverheadMessage(MessageType.Regular, 0x3B2, 501732); // I cannot lock this down!
          }
          else if (item is AddonComponent)
          {
            from.LocalOverheadMessage(MessageType.Regular, 0x3E9, 501727); // You cannot lock that down!
            from.LocalOverheadMessage(MessageType.Regular, 0x3E9, 501732); // I cannot lock this down!
          }
          else
          {
            if (item is AddonContainerComponent component)
            {
              if (component.Addon != null)
                m_House.LockDown(from, component.Addon);
            }
            else
            {
              m_House.LockDown(from, item);
            }
          }
        }
      }
      else if (targeted is StaticTarget)
      {
      }
      else
      {
        from.SendLocalizedMessage(1005377); //You cannot lock that down
      }
    }
  }

  public class SecureTarget : Target
  {
    private BaseHouse m_House;
    private bool m_Release;

    public SecureTarget(bool release, BaseHouse house) : base(12, false, TargetFlags.None)
    {
      CheckLOS = false;

      m_Release = release;
      m_House = house;
    }

    protected override void OnTargetNotAccessible(Mobile from, object targeted)
    {
      OnTarget(from, targeted);
    }

    protected override void OnTarget(Mobile from, object targeted)
    {
      if (!from.Alive || m_House.Deleted || !m_House.IsCoOwner(from))
        return;

      if (targeted is Item item)
      {
        if (m_Release)
        {
          if (item is AddonContainerComponent component)
          {
            if (component.Addon != null)
              m_House.ReleaseSecure(from, component.Addon);
          }
          else
          {
            m_House.ReleaseSecure(from, item);
          }
        }
        else
        {
          if (item is VendorRentalContract)
          {
            from.LocalOverheadMessage(MessageType.Regular, 0x3B2,
              1062392); // You must double click the contract in your pack to lock it down.
            from.LocalOverheadMessage(MessageType.Regular, 0x3B2, 501732); // I cannot lock this down!
          }
          else
          {
            if (item is AddonContainerComponent component)
            {
              if (component.Addon != null)
                m_House.AddSecure(from, component.Addon);
            }
            else
            {
              m_House.AddSecure(from, item);
            }
          }
        }
      }
      else
      {
        from.SendLocalizedMessage(1010424); //You cannot secure this
      }
    }
  }

  public class HouseKickTarget : Target
  {
    private BaseHouse m_House;

    public HouseKickTarget(BaseHouse house) : base(-1, false, TargetFlags.None)
    {
      CheckLOS = false;

      m_House = house;
    }

    protected override void OnTarget(Mobile from, object targeted)
    {
      if (!from.Alive || m_House.Deleted || !m_House.IsFriend(from))
        return;

      if (targeted is Mobile mobile)
        m_House.Kick(from, mobile);
      else
        from.SendLocalizedMessage(501347); //You cannot eject that from the house!
    }
  }

  public class HouseBanTarget : Target
  {
    private bool m_Banning;
    private BaseHouse m_House;

    public HouseBanTarget(bool ban, BaseHouse house) : base(-1, false, TargetFlags.None)
    {
      CheckLOS = false;

      m_House = house;
      m_Banning = ban;
    }

    protected override void OnTarget(Mobile from, object targeted)
    {
      if (!from.Alive || m_House.Deleted || !m_House.IsFriend(from))
        return;

      if (targeted is Mobile mobile)
      {
        if (m_Banning)
          m_House.Ban(from, mobile);
        else
          m_House.RemoveBan(from, mobile);
      }
      else
      {
        from.SendLocalizedMessage(501347); //You cannot eject that from the house!
      }
    }
  }

  public class HouseAccessTarget : Target
  {
    private BaseHouse m_House;

    public HouseAccessTarget(BaseHouse house) : base(-1, false, TargetFlags.None)
    {
      CheckLOS = false;

      m_House = house;
    }

    protected override void OnTarget(Mobile from, object targeted)
    {
      if (!from.Alive || m_House.Deleted || !m_House.IsFriend(from))
        return;

      if (targeted is Mobile mobile)
        m_House.GrantAccess(from, mobile);
      else
        from.SendLocalizedMessage(1060712); // That is not a player.
    }
  }

  public class CoOwnerTarget : Target
  {
    private bool m_Add;
    private BaseHouse m_House;

    public CoOwnerTarget(bool add, BaseHouse house) : base(12, false, TargetFlags.None)
    {
      CheckLOS = false;

      m_House = house;
      m_Add = add;
    }

    protected override void OnTarget(Mobile from, object targeted)
    {
      if (!from.Alive || m_House.Deleted || !m_House.IsOwner(from))
        return;

      if (targeted is Mobile mobile)
      {
        if (m_Add)
          m_House.AddCoOwner(from, mobile);
        else
          m_House.RemoveCoOwner(from, mobile);
      }
      else
      {
        from.SendLocalizedMessage(501362); //That can't be a coowner
      }
    }
  }

  public class HouseFriendTarget : Target
  {
    private bool m_Add;
    private BaseHouse m_House;

    public HouseFriendTarget(bool add, BaseHouse house) : base(12, false, TargetFlags.None)
    {
      CheckLOS = false;

      m_House = house;
      m_Add = add;
    }

    protected override void OnTarget(Mobile from, object targeted)
    {
      if (!from.Alive || m_House.Deleted || !m_House.IsCoOwner(from))
        return;

      if (targeted is Mobile mobile)
      {
        if (m_Add)
          m_House.AddFriend(from, mobile);
        else
          m_House.RemoveFriend(from, mobile);
      }
      else
      {
        from.SendLocalizedMessage(501371); // That can't be a friend
      }
    }
  }

  public class HouseOwnerTarget : Target
  {
    private BaseHouse m_House;

    public HouseOwnerTarget(BaseHouse house) : base(12, false, TargetFlags.None)
    {
      CheckLOS = false;

      m_House = house;
    }

    protected override void OnTarget(Mobile from, object targeted)
    {
      if (targeted is Mobile mobile)
        m_House.BeginConfirmTransfer(from, mobile);
      else
        from.SendLocalizedMessage(501384); // Only a player can own a house!
    }
  }

  #endregion

  public class SetSecureLevelEntry : ContextMenuEntry
  {
    private Item m_Item;
    private ISecurable m_Securable;

    public SetSecureLevelEntry(Item item, ISecurable securable) : base(6203, 6)
    {
      m_Item = item;
      m_Securable = securable;
    }

    public static ISecurable GetSecurable(Mobile from, Item item)
    {
      BaseHouse house = BaseHouse.FindHouseAt(item);

      if (house == null || !house.IsOwner(from) || !house.IsAosRules)
        return null;

      ISecurable sec = null;

      if (item is ISecurable securable)
      {
        bool isOwned = item is BaseDoor door && house.Doors.Contains(door);

        if (!isOwned)
          isOwned = house is HouseFoundation foundation && foundation.IsFixture(item);

        if (!isOwned)
          isOwned = house.HasLockedDownItem(item);

        if (isOwned)
          sec = securable;
      }
      else
      {
        List<SecureInfo> list = house.Secures;

        for (int i = 0; sec == null && list != null && i < list.Count; ++i)
        {
          SecureInfo si = list[i];

          if (si.Item == item)
            sec = si;
        }
      }

      return sec;
    }

    public static void AddTo(Mobile from, Item item, List<ContextMenuEntry> list)
    {
      ISecurable sec = GetSecurable(from, item);

      if (sec != null)
        list.Add(new SetSecureLevelEntry(item, sec));
    }

    public override void OnClick()
    {
      ISecurable sec = GetSecurable(Owner.From, m_Item);

      if (sec != null)
      {
        Owner.From.CloseGump<SetSecureLevelGump>();
        Owner.From.SendGump(new SetSecureLevelGump(Owner.From, sec, BaseHouse.FindHouseAt(m_Item)));
      }
    }
  }

  public class TempNoHousingRegion : BaseRegion
  {
    private Mobile m_RegionOwner;

    public TempNoHousingRegion(BaseHouse house, Mobile regionowner)
      : base(null, house.Map, DefaultPriority, house.Region.Area)
    {
      Register();

      m_RegionOwner = regionowner;

      Timer.DelayCall(house.RestrictedPlacingTime, Unregister);
    }

    public override bool AllowHousing(Mobile from, Point3D p) => from == m_RegionOwner || AccountHandler.CheckAccount(from, m_RegionOwner);
  }
}