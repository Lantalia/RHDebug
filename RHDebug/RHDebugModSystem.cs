using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using HarmonyLib;


namespace RHDebug {
    [HarmonyPatch]
    public class RHDebugModSystem : ModSystem 
    {
        public static int last_message_time = 0;
        public static int message_delta = 30;
        public static bool fixup_broken = true;
        public static bool skip_broken = false;
        public static bool fake_one = false;
        public static bool dump_one = false;
        public static ICoreServerAPI api;
        public Harmony harmony;
        public override bool ShouldLoad(EnumAppSide side) 
        {
            return side == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api) 
        {
            RHDebugModSystem.api = api;
            if (!Harmony.HasAnyPatches(Mod.Info.ModID))
            {
                harmony = new Harmony(Mod.Info.ModID);
                harmony.PatchAll(typeof(RHDebugModSystem).Assembly); 
            }
            base.StartServerSide(api);
            // Placeholder to prove it is working
            api.ChatCommands.Create("rhdebug")
                .WithDescription("server debug controls")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(_ => { return TextCommandResult.Success("Status " + 
                    (fake_one ? "breaking next projectile " : "") + 
                    (skip_broken ? "skipping broken projectiles " : "") + 
                    (fixup_broken ? "fixing up broken projectiles" : "")); })
                .BeginSubCommand("dump")
                    .WithDescription("Dump the next projectile")
                    .HandleWith(_ => { dump_one = true; return TextCommandResult.Success(); })
                .EndSubCommand()

                .BeginSubCommand("fake")
                    .WithDescription("Break the next projectile")
                    .HandleWith(_ => { fake_one = true; return TextCommandResult.Success(); })
                .EndSubCommand()
                .BeginSubCommand("skip")
                    .WithDescription("Toggle skipping broken projectiles")
                    .HandleWith(_ => { skip_broken = !skip_broken; return TextCommandResult.Success(skip_broken ? "on" : "off"); })
                .EndSubCommand()
                .BeginSubCommand("fixup")
                    .WithDescription("Toggle skipping broken projectiles")
                    .HandleWith(_ => { fixup_broken = !fixup_broken; return TextCommandResult.Success(fixup_broken ? "on" : "off"); })
                .EndSubCommand();
        }



        public override void Dispose()
        {
            harmony?.UnpatchAll(Mod.Info.ModID);
        }


        [HarmonyPatch(typeof(EntityProjectile), "ToBytes")]
        class PatchEntityProjectile
        {
            public static bool Prefix(EntityProjectile __instance, out bool __state, BinaryWriter writer, bool forClient)
            {
                if (dump_one)
                {
                    api.Logger.Event("RHDebug dumping a projectile {0} ({1})\n\twith ProjectileStack {2} ({3})\n\tfired by {4} ({5})\n\tat{6}", __instance.Code, __instance.ToString(),
                            __instance.ProjectileStack?.Class, __instance.ProjectileStack?.ToString(), 
                            __instance.FiredBy?.GetName(), __instance.FiredBy?.ToString(),
                            __instance.ServerPos);
                    dump_one = false;
                }
                if (fake_one)
                {
                    api.Logger.Event("RHDebug breaking a projectile");
                    __instance.ProjectileStack = null; // new ItemStack();
                    fake_one = false;
                }
                __state = false;                
                if (__instance?.ProjectileStack == null)
                {
                    int servertime = api.Server.ServerUptimeSeconds;
                    if (last_message_time + message_delta <= servertime)
                    {
                        api.Logger.Event("Null ProjectileStack in EntityProjectile.ToBytes for {0} firedby {1} at {2}", __instance.Code, __instance.FiredBy?.GetName(), __instance.ServerPos);
                        last_message_time = servertime;
                    }

                    if (fixup_broken)
                    {
                        __state = true;
                        __instance.ProjectileStack = new ItemStack(); //provide a temporary dummy itemstack to avoid blowing up?
                    }

                    if (skip_broken)
                    {
                        return false;
                    }
                }
                return true;
            }

            public static void Postfix(EntityProjectile __instance, bool __state, BinaryWriter writer, bool forClient)
            {
                if (__state) // We did a fixup, so, we need to purge the projectile
                {
                    __instance.Die();
                }
            }

        }

    }

}

