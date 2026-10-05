using System.Threading.Tasks;
using candela;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// Rain and strong wind put out candles and chandeliers with the sky over them -
    /// snuffed, fuel kept. Lanterns and anything under a roof are sheltered.
    /// </summary>
    public class CandelaWeather
    {
        static BlockPos Light => P(8, 1, 8);

        [BeforeEach]
        public void Defaults() => CandelaConfig.Current.AssignFrom(new CandelaConfig());

        [AfterEach]
        public void Clear()
        {
            World.SetPrecipitation(null);
            CandelaConfig.Current.AssignFrom(new CandelaConfig());
        }

        /// <summary>
        /// The decision alone. Wind has no override for a test to set, so this is where
        /// it is checked: rain always, strong wind by chance, nothing under a roof.
        /// </summary>
        [VsTest]
        public void WhatPutsAFlameOut()
        {
            Assert.True(Weather.PutsOut(exposed: true, precipitation: 0.3f, wind: 0, roll: 0.99));
            Assert.False(Weather.PutsOut(exposed: true, precipitation: 0.01f, wind: 0.2, roll: 0));
            Assert.True(Weather.PutsOut(exposed: true, precipitation: 0, wind: 0.9, roll: 0.05));
            Assert.False(Weather.PutsOut(exposed: true, precipitation: 0, wind: 0.9, roll: 0.5), "wind should only sometimes win");
            Assert.False(Weather.PutsOut(exposed: true, precipitation: 0, wind: 0.4, roll: 0), "a breeze is not a gale");
            Assert.False(Weather.PutsOut(exposed: false, precipitation: 1f, wind: 1, roll: 0), "a roof keeps all of it off");
        }

        [VsTest]
        public async Task RainPutsOutABunchInTheOpen()
        {
            var be = await Place<BECandles>("game:bunchocandles-2");
            double fuel = be.Fuel;

            World.SetPrecipitation(1f);
            await World.TickNow(Light);

            Assert.True(be.Snuffed, "rain on an open bunch should put it out");
            Assert.Close(be.Fuel, fuel, 0.5, "putting out should keep the fuel");
        }

        [VsTest]
        public async Task ARoofKeepsTheRainOff()
        {
            World.SetBlock("game:cob-none", Light.UpCopy(3));
            var be = await Place<BECandles>("game:bunchocandles-2");

            World.SetPrecipitation(1f);
            await World.TickNow(Light);

            Assert.False(be.Snuffed, "a bunch under a roof should stay lit in the rain");
        }

        [VsTest]
        public async Task RainPutsOutAChandelierInTheOpen()
        {
            var be = await Place<BECandles>("game:chandelier-candle8");

            World.SetPrecipitation(1f);
            await World.TickNow(Light);

            Assert.True(be.Snuffed, "rain on an open chandelier should put it out");
        }

        [VsTest]
        public async Task ALanternIsSheltered()
        {
            World.SetBlock("game:lantern-large-up", Light);
            await Ticks(2);
            var fuel = World.BE<BlockEntity>(Light).GetBehavior<BEBehaviorLanternFuel>();

            World.SetPrecipitation(1f);
            await World.TickNow(Light);

            Assert.True(fuel.Flame.Flaming, "a lantern should stay lit in the rain");
        }

        [VsTest]
        public async Task WeatherCanBeTurnedOff()
        {
            CandelaConfig.Current.WeatherPutsOut = false;
            var be = await Place<BECandles>("game:bunchocandles-2");

            World.SetPrecipitation(1f);
            await World.TickNow(Light);

            Assert.False(be.Snuffed, "with WeatherPutsOut off the rain should not matter");
        }

        static async Task<T> Place<T>(string code) where T : BlockEntity
        {
            World.SetBlock(code, Light);
            await Ticks(2);
            var be = World.BE<T>(Light);
            Assert.NotNull(be, code + " has no block entity");
            return be;
        }
    }
}
