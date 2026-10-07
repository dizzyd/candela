using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// How much candle is left in a bunch, and whether it is burning.
///
/// A bunch is one pool of burn hours rather than one timer per candle: every
/// candle in it is lit together and burns down together, so <c>n</c> candles take
/// <c>n</c> hours from the pool each game hour, and one candle's share is the pool
/// divided by the count. Adding a candle adds its hours; taking one takes a share.
///
/// That only works because quantity changes go through ExchangeBlock, which keeps
/// the block entity. Vanilla's SetBlock would throw the pool away on every candle
/// added or taken - see <see cref="ItemCandelaCandle"/> and
/// <see cref="BlockCandelaCandles"/>, which replace those paths.
///
/// The burning itself is a <see cref="Flame"/>, shared with the lantern.
///
/// Each candle keeps its own look - flame colour and wax dye (<see cref="CandleLook"/>) -
/// in the order the candles went in, so a bunch or a chandelier can mix them. The last one in is
/// the first taken off. Candles with no entry - a bunch from before colours, or one
/// vanilla made - are plain.
/// </summary>
public class BECandles : BlockEntity, IIgnitable
{
    private readonly Flame flame = new();

    // One entry a candle, empty for plain; left out altogether while all are plain.
    private const string FlamesKey = "candela:flames";
    private const string DyesKey = "candela:dyes";

    /// <summary>
    /// Each candle's look, plain by default, by the order they went in. Replaced whole,
    /// never changed in place: the client's particle thread reads it while state from
    /// the server is being applied.
    /// </summary>
    private CandleLook[] looks = [];

    /// <summary>Burn hours left across every candle in the bunch.</summary>
    public double Fuel => flame.Fuel;

    public bool Snuffed => flame.Snuffed;

    public BurnoutMode Mode => flame.Mode;

    public bool Spent => flame.Spent;

    public bool Flaming => flame.Flaming;

    public int Quantity => (Block as ICandleHolder)?.Quantity ?? 1;

    public double FullHours => (Block as ICandleHolder)?.BurnHours ?? 48;

    /// <summary>Candle <paramref name="index"/>'s look, plain if it has none.</summary>
    public CandleLook LookOf(int index)
    {
        CandleLook[] current = looks;
        return index >= 0 && index < current.Length ? current[index] : CandleLook.Plain;
    }

    /// <summary>Every candle's look.</summary>
    public IEnumerable<CandleLook> Looks => Enumerable.Range(0, Quantity).Select(LookOf);

    /// <summary>Every candle's flame colour, null for plain.</summary>
    public IEnumerable<string> FlameColoursOfCandles => Looks.Select(l => l.Flame);

    /// <summary>
    /// How tall the candles stand, as a fraction of new: quarters while there is fuel,
    /// so the mesh changes four times over a candle's life rather than continuously,
    /// and a stub once it is gone.
    /// </summary>
    public float HeightFactor
    {
        get
        {
            if (Mode == BurnoutMode.None) return 1f;
            double full = Quantity * FullHours;
            if (Fuel <= 0 || full <= 0) return SpentHeight;
            return (float)(Math.Ceiling(GameMath.Clamp(Fuel / full, 0, 1) * 4) / 4);
        }
    }

    public const float SpentHeight = 0.15f;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side != EnumAppSide.Server) return;

        // New, or a vanilla bunch from before Candela was installed getting its
        // block entity at last: either way, new candles.
        flame.Initialize(api, Quantity * FullHours);
        RegisterGameTickListener(OnBurnTick, 4000, api.World.Rand.Next(4000));

        // The time it was unloaded burned at once, rather than up to four seconds on.
        RegisterDelayedCallback(_ => Settle(), 0);
    }

    private void OnBurnTick(float dt)
    {
        bool changed = Settle();

        if (Flaming && Weather.PutsOutAt(Api, Pos))
        {
            Snuff();
            return;
        }

        if (!changed) Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    /// <summary>
    /// Burns the time since the last burn, at the number of candles there were over it.
    /// Server side. Everything that changes the pool or the count calls this first -
    /// see <see cref="Flame"/>. Returns true if anything shown changed.
    /// </summary>
    public bool Settle()
    {
        if (Api?.Side != EnumAppSide.Server) return false;

        float heightBefore = HeightFactor;
        bool spentBefore = Spent;
        byte[] light = Relight.Capture(this);

        flame.Burn(Api.World.Calendar.TotalHours, Quantity);

        if (Spent != spentBefore) Changed(light);
        else if (HeightFactor != heightBefore) MarkDirty(true);
        else return false;
        return true;
    }

    /// <summary>The light this bunch gives, given what the block would give new and lit.</summary>
    public byte[] LightHsv(byte[] full) => FlameColours.Tint(flame.LightHsv(full), FlameColours.Prevailing(FlameColoursOfCandles));

    public void Snuff()
    {
        Settle();
        byte[] light = Relight.Capture(this);
        if (flame.Snuff()) Changed(light);
    }

    /// <summary>Lights the bunch, if it has anything left to light.</summary>
    public bool TryIgnite()
    {
        // An empty chandelier has nothing to light.
        if (Quantity <= 0) return false;
        Settle();
        byte[] light = Relight.Capture(this);
        if (!flame.TryIgnite(Api.World.Calendar.TotalHours)) return false;
        Changed(light);
        return true;
    }

    /// <summary>
    /// Adds a candle - its hours to the pool, its flame colour to the end. Call before
    /// exchanging the block for one with one more candle, so the light update that
    /// exchange triggers sees them.
    /// </summary>
    public void AddCandle(double hours, CandleLook look)
    {
        Settle();
        flame.AddFuel(hours);
        SetLooks(Looks.Append(look));
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    /// <summary>
    /// Takes the last candle in: its share of the pool, returned, and its flame
    /// colour. Call before exchanging for one candle fewer.
    /// </summary>
    public double TakeCandle(out CandleLook look)
    {
        Settle();
        look = LookOf(Quantity - 1);
        SetLooks(Looks.Take(Math.Max(0, Quantity - 1)));
        return flame.TakeFuel(Fuel / Math.Max(1, Quantity));
    }

    /// <summary>
    /// Sets the pool outright, all its candles looking <paramref name="look"/> -
    /// for a block just placed from a part-burned candle.
    /// </summary>
    public void SetFuel(double hours, CandleLook look)
    {
        Settle();
        byte[] light = Relight.Capture(this);
        flame.SetFuel(hours);
        SetLooks(Enumerable.Repeat(look, Quantity));
        Changed(light);
    }

    private void SetLooks(IEnumerable<CandleLook> next) => looks = next.ToArray();

    public override void OnExchanged(Block block)
    {
        base.OnExchanged(block);
        if (Api?.Side == EnumAppSide.Server) MarkDirty(true);
    }

    /// <summary>State that affects the light has changed; <paramref name="lightBefore"/> is what it was.</summary>
    private void Changed(byte[] lightBefore)
    {
        MarkDirty(true);
        Relight.After(this, lightBefore);
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (Block is BlockCandelaChandelier chandelier)
        {
            MeshData coloured = chandelier.ColouredMesh(tessThreadTesselator, this);
            if (coloured == null) return false;
            mesher.AddMeshData(coloured);
            return true;
        }

        if (Block is not BlockCandelaCandles candles || Api is not ICoreClientAPI capi) return false;

        MeshData mesh = (candles.ColouredMesh(tessThreadTesselator, this) ?? capi.TesselatorManager.GetDefaultBlockMesh(Block)).Clone();
        int rotation = candles.RotationIndex(Pos);
        if (rotation > 0) mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, rotation * GameMath.PIHALF, 0);

        float height = HeightFactor;
        if (height < 1f) mesh.Scale(new Vec3f(0.5f, 0, 0.5f), 1, height, 1);

        mesher.AddMeshData(mesh);
        return true;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        int count = Math.Max(1, Quantity);
        CandleInfo.Append(dsc, flame, flame.FuelAt(Api.World.Calendar.TotalHours, count) / count);
        CandleInfo.AppendLooks(dsc, Looks.ToList());
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        flame.ToTreeAttributes(tree);
        // One entry a candle, empty for plain; each left out altogether while all are plain.
        SaveList(tree, FlamesKey, looks.Select(l => l.Flame));
        SaveList(tree, DyesKey, looks.Select(l => l.Dye));
    }

    private static void SaveList(ITreeAttribute tree, string key, IEnumerable<string> entries)
    {
        string[] all = entries.ToArray();
        if (all.Any(e => e != null)) tree.SetString(key, string.Join(",", all.Select(e => e ?? "")));
        else tree.RemoveAttribute(key);
    }

    private static string[] LoadList(ITreeAttribute tree, string key) =>
        tree.GetString(key) is string saved && saved.Length > 0 ? saved.Split(',') : [];

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        float heightBefore = HeightFactor;
        bool flamingBefore = Flaming;
        bool spentBefore = Spent;
        string lightColourBefore = FlameColours.Prevailing(FlameColoursOfCandles);
        string looksBefore = string.Join(",", Looks);
        byte[] lightBefore = Api != null && Block != null ? Relight.Capture(this) : null;

        flame.FromTreeAttributes(tree);
        string[] flames = LoadList(tree, FlamesKey), dyes = LoadList(tree, DyesKey);
        SetLooks(Enumerable.Range(0, Math.Max(flames.Length, dyes.Length)).Select(i => new CandleLook(
            i < flames.Length ? FlameColours.Get(flames[i])?.Code : null,
            i < dyes.Length ? WaxDyes.Get(dyes[i]) : null)));

        if (Api is ICoreClientAPI)
        {
            // Redrawn for looks too: a chandelier's flames, a bunch's tips and every
            // candle's wax are in its mesh.
            if (HeightFactor != heightBefore || Flaming != flamingBefore || string.Join(",", Looks) != looksBefore) MarkDirty(true);
            Relight.Synced(this, lightBefore);
            return;
        }

        // State restored onto a block entity already running: a block that fell and
        // landed, or a schematic pasted. The block was lit for whatever state it was
        // placed with - new candles - so a spent or snuffed one would keep shining at
        // full until something else changed. Deferred a tick rather than exchanging
        // the block from inside its own deserialisation. A coloured one was lit plain,
        // the same way.
        if (lightBefore != null && (Flaming != flamingBefore || Spent != spentBefore || FlameColours.Prevailing(FlameColoursOfCandles) != lightColourBefore))
        {
            RegisterDelayedCallback(_ => Changed(lightBefore), 0);
        }
    }

    // An empty chandelier is "flaming" - nothing snuffed it - and must neither light a
    // torch held to it nor take a firestarter. Only the holder knows it is empty: a
    // spent candle with no fuel is meant to gutter on, and to light a torch.
    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting) =>
        Quantity > 0 ? flame.OnTryIgniteBlock(secondsIgniting) : EnumIgniteState.NotIgnitable;

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        if (TryIgnite()) handling = EnumHandling.PreventDefault;
    }

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting) =>
        Quantity > 0 ? flame.OnTryIgniteStack(secondsIgniting) : EnumIgniteState.NotIgnitable;
}

/// <summary>The block info line every candle flame shows.</summary>
public static class CandleInfo
{
    public static void Append(StringBuilder dsc, Flame flame, double hoursPerCandle)
    {
        if (flame.Mode == BurnoutMode.None) return;

        if (flame.Spent) dsc.AppendLine(Lang.Get(flame.Mode == BurnoutMode.Dark ? "candela:candles-out" : "candela:candles-guttering"));
        else dsc.AppendLine(Lang.Get(flame.Snuffed ? "candela:candles-snuffed" : "candela:candles-burning", Math.Max(1, (int)Math.Round(hoursPerCandle))));
    }

    /// <summary>"Flames: 2 green, 1 plain" and "Wax: 1 black, 2 undyed", each when any is coloured.</summary>
    public static void AppendLooks(StringBuilder dsc, IReadOnlyCollection<CandleLook> looks)
    {
        AppendCounts(dsc, "candela:candles-flames", looks.Select(l => l.Flame), "candela:colour-", "plain");
        AppendCounts(dsc, "candela:candles-dyes", looks.Select(l => l.Dye), "candela:dye-", "undyed");
    }

    private static void AppendCounts(StringBuilder dsc, string line, IEnumerable<string> entries, string prefix, string none)
    {
        var groups = entries.GroupBy(e => e).ToList();
        if (groups.All(g => g.Key == null)) return;
        dsc.AppendLine(Lang.Get(line,
            string.Join(", ", groups.Select(g => Lang.Get("candela:candles-flames-count", g.Count(), Lang.Get(prefix + (g.Key ?? none)))))));
    }
}
