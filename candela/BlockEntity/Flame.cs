using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// A candle flame's state, shared by everything that burns candles: hours of fuel,
/// whether a player has put it out, and when it last burned.
///
/// Burning is driven from outside - <see cref="Burn"/> with the game time and how
/// many hours of fuel each game hour costs - so a bunch of five candles and a lantern
/// with one both use this, at rates of five and one.
///
/// Time burns at the rate in force while it passed, so the owner burns up to now
/// before changing anything the rate or the bill depends on - snuffing, adding a
/// candle, taking one. Otherwise a candle added would be billed for hours before it
/// was there, and one snuffed before the first tick after loading would never pay for
/// the time it was unloaded.
/// </summary>
public class Flame
{
    public double Fuel { get; private set; }

    /// <summary>Put out by a player, as opposed to burned down.</summary>
    public bool Snuffed { get; private set; }

    /// <summary>
    /// The server's burnout mode, carried to the client with the rest of the state
    /// so that the client lights and draws a spent flame the way the server judges it.
    /// </summary>
    public BurnoutMode Mode { get; private set; } = BurnoutMode.Dim;

    /// <summary>False until the state has been set or loaded - a block entity just created.</summary>
    public bool HasState { get; private set; }

    private double lastUpdateHours;

    public bool Spent => Fuel <= 0 && Mode != BurnoutMode.None;

    /// <summary>Whether there is a flame: not snuffed, and not burned down into the dark.</summary>
    public bool Flaming => !Snuffed && !(Spent && Mode == BurnoutMode.Dark);

    /// <summary>
    /// Server side, when the owning block entity is initialised: take the burnout
    /// mode from the config, start new with <paramref name="freshFuel"/> if there is
    /// no state yet, and otherwise decide how much of the time spent unloaded to burn.
    /// </summary>
    public void Initialize(ICoreAPI api, double freshFuel)
    {
        CandelaConfig config = CandelaConfig.Current;
        Mode = config.BurnoutMode;
        double now = api.World.Calendar.TotalHours;

        if (!HasState)
        {
            Fuel = freshFuel;
            lastUpdateHours = now;
            HasState = true;
            return;
        }

        lastUpdateHours = config.UnattendedMode switch
        {
            UnattendedMode.LoadedOnly => now,
            UnattendedMode.CappedCatchUp => Math.Max(lastUpdateHours, now - config.CatchUpCapHours),
            _ => lastUpdateHours,
        };
    }

    /// <summary>
    /// Burns the fuel for the time since the last call, at <paramref name="rate"/>
    /// hours of fuel per game hour. Returns true if this call used the last of it.
    /// </summary>
    public bool Burn(double now, double rate)
    {
        double elapsed = now - lastUpdateHours;
        lastUpdateHours = now;

        if (Snuffed || Mode == BurnoutMode.None || Fuel <= 0 || elapsed <= 0) return false;

        Fuel = Math.Max(0, Fuel - elapsed * rate);
        return Fuel <= 0;
    }

    /// <summary>
    /// The fuel there will be at <paramref name="now"/> if it goes on burning at
    /// <paramref name="rate"/>, without burning it. For the client's block info: the
    /// server burns every few seconds but sends the state only when the light or the
    /// candles' height changes.
    /// </summary>
    public double FuelAt(double now, double rate)
    {
        if (Snuffed || Mode == BurnoutMode.None || Fuel <= 0) return Fuel;
        return Math.Max(0, Fuel - Math.Max(0, now - lastUpdateHours) * rate);
    }

    /// <summary>The light given, from what it would give new and lit.</summary>
    public byte[] LightHsv(byte[] full)
    {
        // Nothing to light - a chandelier with no candles in it - stays as it is,
        // rather than gaining the dim floor.
        if (full[2] == 0) return full;
        if (!Flaming) return Dark;
        if (!Spent) return full;

        // Guttering: the dim floor that makes upkeep a matter of brightness rather
        // than of light at all.
        return [full[0], full[1], (byte)Math.Max(2, full[2] / 3)];
    }

    private static readonly byte[] Dark = [0, 0, 0];

    /// <summary>Puts it out. Returns false if it was not burning.</summary>
    public bool Snuff()
    {
        if (!Flaming) return false;
        Snuffed = true;
        return true;
    }

    /// <summary>Lights it, if there is anything left to light. Returns false otherwise.</summary>
    public bool TryIgnite(double now)
    {
        if (!CanIgnite) return false;
        Snuffed = false;
        lastUpdateHours = now;
        return true;
    }

    public bool CanIgnite => !Flaming && !(Spent && Mode == BurnoutMode.Dark);

    public void SetFuel(double hours, bool snuffed = false)
    {
        Fuel = Math.Max(0, hours);
        Snuffed = snuffed;
        HasState = true;
    }

    public void AddFuel(double hours) => Fuel += hours;

    public double TakeFuel(double hours)
    {
        double taken = Math.Min(Fuel, hours);
        Fuel -= taken;
        return taken;
    }

    // IIgnitable, for the owners to delegate to: firestarters light an unlit flame,
    // and a lit one lights a torch held to it.

    public EnumIgniteState OnTryIgniteBlock(float secondsIgniting)
    {
        if (Flaming) return EnumIgniteState.NotIgnitablePreventDefault;
        if (!CanIgnite) return EnumIgniteState.NotIgnitable;
        return secondsIgniting > 2 ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }

    public EnumIgniteState OnTryIgniteStack(float secondsIgniting)
    {
        if (!Flaming) return EnumIgniteState.NotIgnitable;
        return secondsIgniting > 1 ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }

    public void ToTreeAttributes(ITreeAttribute tree)
    {
        tree.SetDouble("candela:fuel", Fuel);
        tree.SetBool("candela:snuffed", Snuffed);
        tree.SetDouble("candela:lastUpdateHours", lastUpdateHours);
        tree.SetInt("candela:mode", (int)Mode);
    }

    public void FromTreeAttributes(ITreeAttribute tree)
    {
        HasState = tree.HasAttribute("candela:fuel");
        Fuel = tree.GetDouble("candela:fuel");
        Snuffed = tree.GetBool("candela:snuffed");
        lastUpdateHours = tree.GetDouble("candela:lastUpdateHours");
        Mode = (BurnoutMode)tree.GetInt("candela:mode", (int)BurnoutMode.Dim);
    }
}
