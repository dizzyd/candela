using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using candela;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// ConfigKit, if the player has it: an in-game settings screen, and the server's
    /// values synced to clients so their tooltips quote the server's burn hours.
    ///
    /// The binding is by reflection, against a method in a mod this one does not
    /// reference. If ConfigKit renamed or resignatured it, Candela would carry on
    /// working and silently stop syncing. That is what these are for.
    ///
    /// They no-op when ConfigKit is not installed, which is the ordinary case. To mean
    /// anything, run them with it in the mod path:
    ///
    ///     run.sh ../candela/tests --mod ../candela/candela --mods ../configkit/configkit/bin/Release/Mods
    /// </summary>
    public class CompatConfigKit
    {
        static bool Installed => Sapi.ModLoader.IsModEnabled("configkit");

        static bool Absent(string what)
        {
            if (Installed) return false;
            Log($"  ConfigKit not installed - {what} not checked");
            return true;
        }

        [VsTest]
        public void TheConfigIsHandedToConfigKit()
        {
            if (Absent("the binding")) return;

            Assert.True(CandelaModSystem.ConfigKitBound,
                "the config reached ConfigKit - if this is false the method was not found or threw, "
                + "and the server's settings will not sync to clients");
        }

        [VsTest]
        public void ConfigKitStillHasTheMethodWeCallByName()
        {
            if (Absent("the method")) return;

            var system = Sapi.ModLoader.GetModSystem("ConfigKit.ConfigKitModSystem");
            Assert.NotNull(system, "ConfigKit.ConfigKitModSystem still exists under that name");

            MethodInfo register = system.GetType().GetMethod("RegisterManagedConfig");
            Assert.NotNull(register, "RegisterManagedConfig still exists");

            var types = register.GetParameters().Select(p => p.ParameterType.Name).ToArray();
            Log("  RegisterManagedConfig(" + string.Join(", ", types) + ")");
            Assert.Equal(6, types.Length, "and still takes the six arguments this mod passes");
        }

        /// <summary>
        /// Every setting reaches ConfigKit, and an edit there - a number, and an enum
        /// picked from its dropdown - lands in the config Candela reads, and in the next
        /// candle placed. Done the way ConfigKit's own screen does it: set the setting's
        /// value, then assign the settings onto the registered object.
        ///
        /// By reflection, so this file still compiles in a run without ConfigKit.
        /// </summary>
        [VsTest]
        public async Task AnEditInConfigKitReachesCandela()
        {
            if (Absent("editing")) return;

            object system = Sapi.ModLoader.GetModSystem("ConfigKit.ConfigKitModSystem");
            object config = Call(system, "GetConfig", "candela");
            Assert.NotNull(config, "ConfigKit has no config for candela");

            foreach (var field in typeof(CandelaConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.NotNull(Call(config, "GetSetting", field.Name), "ConfigKit has no setting " + field.Name);
            }

            object hours = Call(config, "GetSetting", "BeeswaxBurnHours");
            object mode = Call(config, "GetSetting", "BurnoutMode");
            try
            {
                hours.GetType().GetProperty("Value").SetValue(hours, new JsonObject(new JValue(50.0)));
                mode.GetType().GetProperty("MappingKey").SetValue(mode, "Dark");
                Call(config, "AssignSettingsValues", CandelaConfig.Current);

                Assert.Equal(50.0, CandelaConfig.Current.BeeswaxBurnHours);
                Assert.Equal(BurnoutMode.Dark, CandelaConfig.Current.BurnoutMode);

                var pos = P(8, 1, 8);
                World.SetBlock("game:bunchocandles-2", pos);
                await Ticks(2);
                Assert.Equal(100.0, World.BE<BECandles>(pos).Fuel);
                Assert.Equal(BurnoutMode.Dark, World.BE<BECandles>(pos).Mode);
            }
            finally
            {
                hours.GetType().GetProperty("Value").SetValue(hours, new JsonObject(new JValue(96.0)));
                mode.GetType().GetProperty("MappingKey").SetValue(mode, "Dim");
                CandelaConfig.Current.AssignFrom(new CandelaConfig());
            }
        }

        static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethods().First(m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(target, args);

        /// <summary>
        /// ConfigKit reflects over the config object, so these attributes are the whole
        /// schema. A setting without them shows up as a bare name with no explanation.
        /// </summary>
        [VsTest]
        public void EverySettingIsDescribedForTheScreen()
        {
            var fields = typeof(CandelaConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
            var undescribed = fields.Where(f => f.GetCustomAttribute<DescriptionAttribute>() == null).Select(f => f.Name).ToArray();

            Log($"  {fields.Length} settings, {undescribed.Length} without a description");
            Assert.Equal(0, undescribed.Length, "every setting is described: " + string.Join(", ", undescribed));
        }
    }
}
