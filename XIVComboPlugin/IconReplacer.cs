using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Game;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Structs.JobGauge;
using Dalamud.Hooking;

namespace XIVComboExpandedPlugin
{
    internal class IconReplacer
    {
        private readonly ClientState ClientState;
        private readonly PluginAddressResolver Address;
        private readonly XIVComboExpandedConfiguration Configuration;

        private delegate ulong IsIconReplaceableDelegate(uint actionID);
        private delegate ulong GetIconDelegate(byte param1, uint param2);

        private readonly Hook<IsIconReplaceableDelegate> IsIconReplaceableHook;
        private readonly Hook<GetIconDelegate> GetIconHook;

        private readonly HashSet<uint> CustomIds = new();

        public IconReplacer(ClientState clientState, SigScanner scanner, XIVComboExpandedConfiguration configuration)
        {
            ClientState = clientState;
            Configuration = configuration;

            Address = new PluginAddressResolver();
            Address.Setup(scanner);

            UpdateEnabledActionIDs();

            GetIconHook = new Hook<GetIconDelegate>(Address.GetIcon, new GetIconDelegate(GetIconDetour), this);
            IsIconReplaceableHook = new Hook<IsIconReplaceableDelegate>(Address.IsIconReplaceable, new IsIconReplaceableDelegate(IsIconReplaceableDetour), this);

            GetIconHook.Enable();
            IsIconReplaceableHook.Enable();
        }

        internal void Dispose()
        {
            GetIconHook.Dispose();
            IsIconReplaceableHook.Dispose();
        }

        /// <summary>
        /// Maps to <see cref="XIVComboExpandedConfiguration.EnabledActions"/>, these actions can potentially update their icon per the user configuration.
        /// </summary>
        public void UpdateEnabledActionIDs()
        {
            var actionIDs = Enum
                .GetValues(typeof(CustomComboPreset))
                .Cast<CustomComboPreset>()
                .Select(preset => preset.GetAttribute<CustomComboInfoAttribute>())
                .OfType<CustomComboInfoAttribute>()
                .SelectMany(comboInfo => comboInfo.Abilities)
                .ToHashSet();
            CustomIds.Clear();
            CustomIds.UnionWith(actionIDs);
        }

        private T GetJobGauge<T>() => ClientState.JobGauges.Get<T>();

        private ulong IsIconReplaceableDetour(uint actionID) => 1;

        /// <summary>
        ///     Replace an ability with another ability
        ///     actionID is the original ability to be "used"
        ///     Return either actionID (itself) or a new Action table ID as the
        ///     ability to take its place.
        ///     I tend to make the "combo chain" button be the last move in the combo
        ///     For example, Souleater combo on DRK happens by dragging Souleater
        ///     onto your bar and mashing it.
        /// </summary>
        private ulong GetIconDetour(byte self, uint actionID)
        {
            if (ClientState.LocalPlayer == null)
                return GetIconHook.Original(self, actionID);

            if (!CustomIds.Contains(actionID))
                return GetIconHook.Original(self, actionID);

            var lastMove = Marshal.ReadInt32(Address.LastComboMove);
            var comboTime = Marshal.PtrToStructure<float>(Address.ComboTimer);
            var level = ClientState.LocalPlayer.Level;

            // ====================================================================================
            #region DRAGOON

            // Change Jump/High Jump into Mirage Dive when Dive Ready
            if (Configuration.IsEnabled(CustomComboPreset.DragoonJumpFeature))
            {
                if (actionID == DRG.Jump)
                {
                    if (HasBuff(DRG.Buffs.DiveReady))
                        return DRG.MirageDive;
                    if (level >= DRG.Levels.HighJump)
                        return DRG.HighJump;
                    return DRG.Jump;
                }
            }

            // Change Blood of the Dragon into Stardiver when in Life of the Dragon
            if (Configuration.IsEnabled(CustomComboPreset.DragoonBOTDFeature))
            {
                if (actionID == DRG.BloodOfTheDragon)
                {
                    if (level >= DRG.Levels.Stardiver)
                    {
                        var gauge = GetJobGauge<DRGGauge>();
                        if (gauge.BOTDState == BOTDState.LOTD)
                            return DRG.Stardiver;
                    }
                    return DRG.BloodOfTheDragon;
                }
            }

            // Replace Coerthan Torment with Coerthan Torment combo chain
            if (Configuration.IsEnabled(CustomComboPreset.DragoonCoerthanTormentCombo))
            {
                if (actionID == DRG.CoerthanTorment)
                {
                    if (comboTime > 0)
                    {
                        if (lastMove == DRG.DoomSpike && level >= DRG.Levels.SonicThrust)
                            return DRG.SonicThrust;
                        if (lastMove == DRG.SonicThrust && level >= DRG.Levels.CoerthanTorment)
                            return DRG.CoerthanTorment;
                    }
                    return DRG.DoomSpike;
                }
            }

            // Replace Chaos Thrust with the Chaos Thrust combo chain
            if (Configuration.IsEnabled(CustomComboPreset.DragoonChaosThrustCombo))
            {
                if (actionID == DRG.ChaosThrust)
                {
                    if (comboTime > 0)
                    {
                        if ((lastMove == DRG.TrueThrust || lastMove == DRG.RaidenThrust) && level >= DRG.Levels.Disembowel)
                            return DRG.Disembowel;
                        if (lastMove == DRG.Disembowel && level >= DRG.Levels.ChaosThrust)
                            return DRG.ChaosThrust;
                    }
                    if (HasBuff(DRG.Buffs.SharperFangAndClaw) && level >= DRG.Levels.FangAndClaw)
                        return DRG.FangAndClaw;
                    if (HasBuff(DRG.Buffs.EnhancedWheelingThrust) && level >= DRG.Levels.WheelingThrust)
                        return DRG.WheelingThrust;
                    if (HasBuff(DRG.Buffs.RaidenThrustReady) && level >= DRG.Levels.RaidenThrust)
                        return DRG.RaidenThrust;
                    return DRG.TrueThrust;
                }
            }

            // Replace Full Thrust with the Full Thrust combo chain
            if (Configuration.IsEnabled(CustomComboPreset.DragoonFullThrustCombo))
            {
                if (actionID == DRG.FullThrust)
                {
                    if (comboTime > 0)
                    {
                        if ((lastMove == DRG.TrueThrust || lastMove == DRG.RaidenThrust)
                            && level >= DRG.Levels.VorpalThrust)
                            return DRG.VorpalThrust;
                        if (lastMove == DRG.VorpalThrust && level >= DRG.Levels.FullThrust)
                            return DRG.FullThrust;
                    }
                    if (HasBuff(DRG.Buffs.SharperFangAndClaw) && level >= DRG.Levels.FangAndClaw)
                        return DRG.FangAndClaw;
                    if (HasBuff(DRG.Buffs.EnhancedWheelingThrust) && level >= DRG.Levels.WheelingThrust)
                        return DRG.WheelingThrust;
                    if (HasBuff(DRG.Buffs.RaidenThrustReady) && level >= DRG.Levels.RaidenThrust)
                        return DRG.RaidenThrust;
                    return DRG.TrueThrust;
                }
            }

            #endregion
            // ====================================================================================
            #region DARK KNIGHT

            // Replace Souleater with Souleater combo chain
            if (Configuration.IsEnabled(CustomComboPreset.DarkSouleaterCombo))
            {
                if (actionID == DRK.Souleater)
                {
                    if (Configuration.IsEnabled(CustomComboPreset.DeliriumFeature))
                        if (level >= DRK.Levels.Bloodpiller && level >= DRK.Levels.Delirium && HasBuff(DRK.Buffs.Delirium))
                            return DRK.Bloodspiller;

                    if (comboTime > 0)
                    {
                        if (lastMove == DRK.HardSlash && level >= DRK.Levels.SyphonStrike)
                            return DRK.SyphonStrike;
                        if (lastMove == DRK.SyphonStrike && level >= DRK.Levels.Souleater)
                            return DRK.Souleater;
                    }
                    return DRK.HardSlash;
                }
            }

            // Replace Stalwart Soul with Stalwart Soul combo chain
            if (Configuration.IsEnabled(CustomComboPreset.DarkStalwartSoulCombo))
            {
                if (actionID == DRK.StalwartSoul)
                {
                    if (Configuration.IsEnabled(CustomComboPreset.DeliriumFeature))
                        if (level >= DRK.Levels.Quietus && level >= DRK.Levels.Delirium && HasBuff(DRK.Buffs.Delirium))
                            return DRK.Quietus;

                    if (comboTime > 0)
                        if (lastMove == DRK.Unleash && level >= DRK.Levels.StalwartSoul)
                            return DRK.StalwartSoul;

                    return DRK.Unleash;
                }
            }

            #endregion
            // ====================================================================================
            #region PALADIN

            // Replace Goring Blade with Goring Blade combo
            if (Configuration.IsEnabled(CustomComboPreset.PaladinGoringBladeCombo))
            {
                if (actionID == PLD.GoringBlade)
                {
                    if (comboTime > 0)
                    {
                        if (lastMove == PLD.FastBlade && level >= PLD.Levels.RiotBlade)
                            return PLD.RiotBlade;
                        if (lastMove == PLD.RiotBlade && level >= PLD.Levels.GoringBlade)
                            return PLD.GoringBlade;
                    }

                    return PLD.FastBlade;
                }
            }

            // Replace Royal Authority with Royal Authority combo
            if (Configuration.IsEnabled(CustomComboPreset.PaladinRoyalAuthorityCombo))
            {
                if (actionID == PLD.RoyalAuthority || actionID == PLD.RageOfHalone)
                {
                    if (comboTime > 0)
                    {
                        if (lastMove == PLD.FastBlade && level >= PLD.Levels.RiotBlade)
                            return PLD.RiotBlade;
                        if (lastMove == PLD.RiotBlade)
                        {
                            if (level >= PLD.Levels.RoyalAuthority)
                                return PLD.RoyalAuthority;
                            if (level >= PLD.Levels.RageOfHalone)
                                return PLD.RageOfHalone;
                        }
                    }

                    if (Configuration.IsEnabled(CustomComboPreset.PaladinAtonementFeature))
                    {
                        if (HasBuff(PLD.Buffs.SwordOath))
                            return PLD.Atonement;
                    }

                    return PLD.FastBlade;
                }
            }

            // Replace Prominence with Prominence combo
            if (Configuration.IsEnabled(CustomComboPreset.PaladinProminenceCombo))
            {
                if (actionID == PLD.Prominence)
                {
                    if (comboTime > 0)
                        if (lastMove == PLD.TotalEclipse && level >= PLD.Levels.Prominence)
                            return PLD.Prominence;

                    return PLD.TotalEclipse;
                }
            }

            // Replace Requiescat with Confiteor when under the effect of Requiescat
            if (Configuration.IsEnabled(CustomComboPreset.PaladinRequiescatCombo))
            {
                if (actionID == PLD.Requiescat)
                {
                    if (HasBuff(PLD.Buffs.Requiescat) && level >= PLD.Levels.Confiteor)
                        return PLD.Confiteor;
                    return PLD.Requiescat;
                }
            }

            #endregion
            // ====================================================================================
            #region WARRIOR

            // Replace Storm's Path with Storm's Path combo
            if (Configuration.IsEnabled(CustomComboPreset.WarriorStormsPathCombo))
            {
                if (actionID == WAR.StormsPath)
                {
                    if (comboTime > 0)
                    {
                        if (lastMove == WAR.HeavySwing && level >= WAR.Levels.Maim)
                            return WAR.Maim;
                        if (lastMove == WAR.Maim && level >= WAR.Levels.StormsPath)
                            return WAR.StormsPath;
                    }
                    return WAR.HeavySwing;
                }
            }

            // Replace Storm's Eye with Storm's Eye combo
            if (Configuration.IsEnabled(CustomComboPreset.WarriorStormsEyeCombo))
            {
                if (actionID == WAR.StormsEye)
                {
                    if (comboTime > 0)
                    {
                        if (lastMove == WAR.HeavySwing && level >= WAR.Levels.Maim)
                            return WAR.Maim;
                        if (lastMove == WAR.Maim && level >= WAR.Levels.StormsEye)
                            return WAR.StormsEye;
                    }
                    return WAR.HeavySwing;
                }
            }

            // Replace Mythril Tempest with Mythril Tempest combo
            if (Configuration.IsEnabled(CustomComboPreset.WarriorMythrilTempestCombo))
            {
                if (actionID == WAR.MythrilTempest)
                {
                    if (comboTime > 0)
                        if (lastMove == WAR.Overpower && level >= WAR.Levels.MythrilTempest)
                            return WAR.MythrilTempest;
                    return WAR.Overpower;
                }
            }

            // Replace Nascent Flash with Raw Intuition if below level 76
            if (Configuration.IsEnabled(CustomComboPreset.WarriorNascentFlashFeature))
            {
                if (actionID == WAR.NascentFlash)
                {
                    if (level >= WAR.Levels.NascentFlash)
                        return WAR.NascentFlash;
                    return WAR.RawIntuition;
                }
            }

            #endregion
            // ====================================================================================
            #region SAMURAI

            // Replace Yukikaze with Yukikaze combo
            if (Configuration.IsEnabled(CustomComboPreset.SamuraiYukikazeCombo))
            {
                if (actionID == SAM.Yukikaze)
                {
                    if (HasBuff(SAM.Buffs.MeikyoShisui))
                        return SAM.Yukikaze;
                    if (comboTime > 0)
                        if (lastMove == SAM.Hakaze && level >= SAM.Levels.Yukikaze)
                            return SAM.Yukikaze;
                    return SAM.Hakaze;
                }
            }

            // Replace Gekko with Gekko combo
            if (Configuration.IsEnabled(CustomComboPreset.SamuraiGekkoCombo))
            {
                if (actionID == SAM.Gekko)
                {
                    if (HasBuff(SAM.Buffs.MeikyoShisui))
                        return SAM.Gekko;
                    if (comboTime > 0)
                    {
                        if (lastMove == SAM.Hakaze && level >= SAM.Levels.Jinpu)
                            return SAM.Jinpu;
                        if (lastMove == SAM.Jinpu && level >= SAM.Levels.Gekko)
                            return SAM.Gekko;
                    }
                    return SAM.Hakaze;
                }
            }

            // Replace Kasha with Kasha combo
            if (Configuration.IsEnabled(CustomComboPreset.SamuraiKashaCombo))
            {
                if (actionID == SAM.Kasha)
                {
                    if (HasBuff(SAM.Buffs.MeikyoShisui))
                        return SAM.Kasha;
                    if (comboTime > 0)
                    {
                        if (lastMove == SAM.Hakaze && level >= SAM.Levels.Shifu)
                            return SAM.Shifu;
                        if (lastMove == SAM.Shifu && level >= SAM.Levels.Kasha)
                            return SAM.Kasha;
                    }
                    return SAM.Hakaze;
                }
            }

            // Replace Mangetsu with Mangetsu combo
            if (Configuration.IsEnabled(CustomComboPreset.SamuraiMangetsuCombo))
            {
                if (actionID == SAM.Mangetsu)
                {
                    if (HasBuff(SAM.Buffs.MeikyoShisui))
                        return SAM.Mangetsu;
                    if (comboTime > 0)
                        if (lastMove == SAM.Fuga && level >= SAM.Levels.Mangetsu)
                            return SAM.Mangetsu;
                    return SAM.Fuga;
                }
            }

            // Replace Oka with Oka combo
            if (Configuration.IsEnabled(CustomComboPreset.SamuraiOkaCombo))
            {
                if (actionID == SAM.Oka)
                {
                    if (HasBuff(SAM.Buffs.MeikyoShisui))
                        return SAM.Oka;
                    if (comboTime > 0)
                        if (lastMove == SAM.Fuga && level >= SAM.Levels.Oka)
                            return SAM.Oka;
                    return SAM.Fuga;
                }
            }

            // Turn Seigan into Third Eye when not procced
            if (Configuration.IsEnabled(CustomComboPreset.SamuraiThirdEyeFeature))
            {
                if (actionID == SAM.Seigan)
                {
                    if (HasBuff(SAM.Buffs.EyesOpen))
                        return SAM.Seigan;
                    return SAM.ThirdEye;
                }
            }

            // Turn Tsubame-gaeshi into Shoha when meditation is 3, by grammernatzi
            if (Configuration.IsEnabled(CustomComboPreset.SamuraiTsubameGaeshiShohaFeature) && actionID == SAM.TsubameGaeshi ||
                Configuration.IsEnabled(CustomComboPreset.SamuraiIaijutsuShohaFeature) && actionID == SAM.Iaijutsu)
            {
                var gauge = GetJobGauge<SAMGauge>();
                if (level >= SAM.Levels.Shoha && gauge.MeditationStacks >= 3)
                    return SAM.Shoha;
            }

            // Turn Tsubame-gaeshi into Iaijutsu when Sen is empty, requested by Sable
            if (Configuration.IsEnabled(CustomComboPreset.SamuraiTsubameGaeshiIaijutsuFeature) && actionID == SAM.TsubameGaeshi ||
                Configuration.IsEnabled(CustomComboPreset.SamuraiIaijutsuTsubameGaeshiFeature) && actionID == SAM.Iaijutsu)
            {
                var gauge = GetJobGauge<SAMGauge>();
                if (level >= SAM.Levels.TsubameGaeshi && gauge.Sen == Sen.NONE)
                {
                    return GetIconHook.Original(self, SAM.TsubameGaeshi);
                }
                else
                {
                    return GetIconHook.Original(self, SAM.Iaijutsu);
                }
            }

            #endregion
            // ====================================================================================
            #region NINJA

            // Replace Armor Crush with Armor Crush combo
            if (Configuration.IsEnabled(CustomComboPreset.NinjaArmorCrushCombo))
            {
                if (actionID == NIN.ArmorCrush)
                {
                    if (comboTime > 0)
                    {
                        if (lastMove == NIN.SpinningEdge && level >= NIN.Levels.GustSlash)
                            return NIN.GustSlash;
                        if (lastMove == NIN.GustSlash && level >= NIN.Levels.ArmorCrush)
                            return NIN.ArmorCrush;
                    }
                    return NIN.SpinningEdge;
                }
            }

            // Replace Aeolian Edge with Aeolian Edge combo
            if (Configuration.IsEnabled(CustomComboPreset.NinjaAeolianEdgeCombo))
            {
                if (actionID == NIN.AeolianEdge)
                {
                    if (comboTime > 0)
                    {
                        if (lastMove == NIN.SpinningEdge && level >= NIN.Levels.GustSlash)
                            return NIN.GustSlash;
                        if (lastMove == NIN.GustSlash && level >= NIN.Levels.AeolianEdge)
                            return NIN.AeolianEdge;
                    }
                    return NIN.SpinningEdge;
                }
            }

            // Replace Hakke Mujinsatsu with Hakke Mujinsatsu combo
            if (Configuration.IsEnabled(CustomComboPreset.NinjaHakkeMujinsatsuCombo))
            {
                if (actionID == NIN.HakkeMujinsatsu)
                {
                    if (comboTime > 0)
                        if (lastMove == NIN.DeathBlossom && level >= NIN.Levels.HakkeMujinsatsu)
                            return NIN.HakkeMujinsatsu;
                    return NIN.DeathBlossom;
                }
            }

            //Replace Dream Within a Dream with Assassinate when Assassinate Ready
            if (Configuration.IsEnabled(CustomComboPreset.NinjaAssassinateFeature))
            {
                if (actionID == NIN.DreamWithinADream)
                {
                    if (HasBuff(NIN.Buffs.AssassinateReady))
                        return NIN.Assassinate;
                    return NIN.DreamWithinADream;
                }
            }

            #endregion
            // ====================================================================================
            #region GUNBREAKER

            // Replace Solid Barrel with Solid Barrel combo
            if (Configuration.IsEnabled(CustomComboPreset.GunbreakerSolidBarrelCombo))
            {
                if (actionID == GNB.SolidBarrel)
                {
                    if (comboTime > 0)
                    {
                        if (lastMove == GNB.KeenEdge && level >= GNB.Levels.BrutalShell)
                            return GNB.BrutalShell;
                        if (lastMove == GNB.BrutalShell && level >= GNB.Levels.SolidBarrel)
                            return GNB.SolidBarrel;
                    }
                    return GNB.KeenEdge;
                }
            }

            // Replace Wicked Talon with Gnashing Fang combo
            if (Configuration.IsEnabled(CustomComboPreset.GunbreakerGnashingFangCombo))
            {
                if (actionID == GNB.WickedTalon)
                {
                    if (Configuration.IsEnabled(CustomComboPreset.GunbreakerGnashingFangCont))
                    {
                        if (level >= GNB.Levels.Continuation)
                        {
                            if (HasBuff(GNB.Buffs.ReadyToRip))
                                return GNB.JugularRip;
                            if (HasBuff(GNB.Buffs.ReadyToTear))
                                return GNB.AbdomenTear;
                            if (HasBuff(GNB.Buffs.ReadyToGouge))
                                return GNB.EyeGouge;
                        }
                    }
                    var ammoComboState = GetJobGauge<GNBGauge>().AmmoComboStepNumber;
                    return ammoComboState switch
                    {
                        1 => GNB.SavageClaw,
                        2 => GNB.WickedTalon,
                        _ => GNB.GnashingFang,
                    };
                }
            }

            // Replace Demon Slaughter with Demon Slaughter combo
            if (Configuration.IsEnabled(CustomComboPreset.GunbreakerDemonSlaughterCombo))
            {
                if (actionID == GNB.DemonSlaughter)
                {
                    if (comboTime > 0)
                        if (lastMove == GNB.DemonSlice && level >= GNB.Levels.DemonSlaughter)
                        {
                            if (Configuration.IsEnabled(CustomComboPreset.GunbreakerFatedCircleFeature))
                            {
                                var gauge = GetJobGauge<GNBGauge>();
                                if (gauge.NumAmmo == 2 && level >= GNB.Levels.FatedCircle)
                                {
                                    return GNB.FatedCircle;
                                }
                            }
                            return GNB.DemonSlaughter;
                        }
                    return GNB.DemonSlice;
                }
            }

            #endregion
            // ====================================================================================
            #region MACHINIST

            // Replace Clean Shot with Heated Clean Shot combo
            // Or with Heat Blast when overheated.
            // For some reason the shots use their unheated IDs as combo moves
            if (Configuration.IsEnabled(CustomComboPreset.MachinistMainCombo))
            {
                if (actionID == MCH.CleanShot || actionID == MCH.HeatedCleanShot)
                {
                    if (comboTime > 0)
                    {
                        if (lastMove == MCH.SplitShot)
                        {
                            if (level >= MCH.Levels.HeatedSlugshot)
                                return MCH.HeatedSlugshot;
                            if (level >= MCH.Levels.SlugShot)
                                return MCH.SlugShot;
                        }
                        if (lastMove == MCH.SlugShot)
                        {
                            if (level >= MCH.Levels.HeatedCleanShot)
                                return MCH.HeatedCleanShot;
                            if (level >= MCH.Levels.CleanShot)
                                return MCH.CleanShot;
                        }
                    }
                    if (level >= MCH.Levels.HeatedSplitShot)
                        return MCH.HeatedSplitShot;
                    return MCH.SplitShot;
                }
            }

            // Replace Heat Blast and Auto crossbow with Hypercharge when not overheated
            if (Configuration.IsEnabled(CustomComboPreset.MachinistOverheatFeature))
            {
                if (actionID == MCH.HeatBlast || actionID == MCH.AutoCrossbow)
                {
                    var gauge = GetJobGauge<MCHGauge>();
                    if (!gauge.IsOverheated() && level >= MCH.Levels.Hypercharge)
                        return MCH.Hypercharge;
                    if (level < MCH.Levels.AutoCrossbow)
                        return MCH.HeatBlast;
                }
            }

            // Replace Spread Shot with Auto Crossbow when overheated.
            if (Configuration.IsEnabled(CustomComboPreset.MachinistSpreadShotFeature))
            {
                if (actionID == MCH.SpreadShot)
                {
                    if (GetJobGauge<MCHGauge>().IsOverheated() && level >= MCH.Levels.AutoCrossbow)
                        return MCH.AutoCrossbow;
                }
            }

            // Replace Rook Turret and Automaton Queen with Overdrive while active.
            if (Configuration.IsEnabled(CustomComboPreset.MachinistOverdriveFeature))
            {
                if (actionID == MCH.RookAutoturret || actionID == MCH.AutomatonQueen)
                {
                    if (GetJobGauge<MCHGauge>().IsRobotActive())
                    {
                        if (level >= MCH.Levels.QueenOverdrive)
                            return MCH.QueenOverdrive;
                        if (level >= MCH.Levels.RookOverdrive)
                            return MCH.RookOverdrive;
                    }
                }
            }

            #endregion
            // ====================================================================================
            #region BLACK MAGE

            // Enochian changes to B4 or F4 depending on stance.
            if (Configuration.IsEnabled(CustomComboPreset.BlackEnochianFeature))
            {
                if (actionID == BLM.Enochian)
                {
                    var gauge = GetJobGauge<BLMGauge>();
                    if (gauge.IsEnoActive())
                    {
                        if (gauge.InUmbralIce() && level >= BLM.Levels.Blizzard4)
                            return BLM.Blizzard4;
                        if (level >= BLM.Levels.Fire4)
                            return BLM.Fire4;
                    }

                    return BLM.Enochian;
                }
            }

            // Umbral Soul and Transpose
            if (Configuration.IsEnabled(CustomComboPreset.BlackManaFeature))
            {
                if (actionID == BLM.Transpose)
                {
                    var gauge = GetJobGauge<BLMGauge>();
                    if (gauge.InUmbralIce() && gauge.IsEnoActive() && level >= BLM.Levels.UmbralSoul)
                        return BLM.UmbralSoul;
                    return BLM.Transpose;
                }
            }

            // Ley Lines and BTL
            if (Configuration.IsEnabled(CustomComboPreset.BlackLeyLines))
            {
                if (actionID == BLM.LeyLines)
                {
                    if (HasBuff(BLM.Buffs.LeyLines) && level >= BLM.Levels.BetweenTheLines)
                        return BLM.BetweenTheLines;
                    return BLM.LeyLines;
                }
            }

            #endregion
            // ====================================================================================
            #region ASTROLOGIAN

            // Make cards on the same button as play
            if (Configuration.IsEnabled(CustomComboPreset.AstrologianCardsOnDrawFeature))
            {
                if (actionID == AST.Play)
                {
                    var gauge = GetJobGauge<ASTGauge>();
                    return gauge.DrawnCard() switch
                    {
                        CardType.BALANCE => AST.Balance,
                        CardType.BOLE => AST.Bole,
                        CardType.ARROW => AST.Arrow,
                        CardType.SPEAR => AST.Spear,
                        CardType.EWER => AST.Ewer,
                        CardType.SPIRE => AST.Spire,
                        // CardType.LORD => AST.LordOfCrowns,
                        // CardType.LADY => AST.LadyOfCrowns,
                        _ => AST.Draw,
                    };
                }
            }

            #endregion
            // ====================================================================================
            #region SUMMONER

            if (Configuration.IsEnabled(CustomComboPreset.SummonerDemiCombo))
            {
                // Replace Deathflare with demi enkindles
                if (actionID == SMN.Deathflare)
                {
                    var gauge = GetJobGauge<SMNGauge>();
                    if (gauge.IsPhoenixReady())
                        return SMN.EnkindlePhoenix;
                    if (gauge.TimerRemaining > 0 && gauge.ReturnSummon != SummonPet.NONE)
                        return SMN.EnkindleBahamut;
                    return SMN.Deathflare;
                }

                //Replace DWT with demi summons
                if (actionID == SMN.DreadwyrmTrance)
                {
                    var gauge = GetJobGauge<SMNGauge>();
                    if (gauge.IsBahamutReady())
                        return SMN.SummonBahamut;
                    if (gauge.IsPhoenixReady() ||
                        gauge.TimerRemaining > 0 && gauge.ReturnSummon != SummonPet.NONE)
                    {
                        if (level >= SMN.Levels.EnhancedFirebirdTrance)
                            return SMN.FirebirdTranceHigh;
                        return SMN.FirebirdTranceLow;
                    }
                    return SMN.DreadwyrmTrance;
                }
            }

            // Ruin 1 now upgrades to Brand of Purgatory in addition to Ruin 3 and Fountain of Fire
            if (Configuration.IsEnabled(CustomComboPreset.SummonerBoPCombo))
            {
                if (actionID == SMN.Ruin1 || actionID == SMN.Ruin3)
                {
                    var gauge = GetJobGauge<SMNGauge>();
                    if (gauge.TimerRemaining > 0)
                        if (gauge.IsPhoenixReady())
                        {
                            if (HasBuff(SMN.Buffs.HellishConduit))
                                return SMN.BrandOfPurgatory;
                            return SMN.FountainOfFire;
                        }

                    if (level >= SMN.Levels.Ruin3)
                        return SMN.Ruin3;
                    return SMN.Ruin1;
                }
            }

            // Change Fester into Energy Drain
            if (Configuration.IsEnabled(CustomComboPreset.SummonerEDFesterCombo))
            {
                if (actionID == SMN.Fester)
                {
                    if (!GetJobGauge<SMNGauge>().HasAetherflowStacks())
                        return SMN.EnergyDrain;
                    return SMN.Fester;
                }
            }

            //Change Painflare into Energy Syphon
            if (Configuration.IsEnabled(CustomComboPreset.SummonerESPainflareCombo))
            {
                if (actionID == SMN.Painflare)
                {
                    if (!GetJobGauge<SMNGauge>().HasAetherflowStacks())
                        return SMN.EnergySyphon;
                    if (level >= SMN.Levels.Painflare)
                        return SMN.Painflare;
                    return SMN.EnergySyphon;
                }
            }

            #endregion
            // ====================================================================================
            #region SCHOLAR

            // Change Fey Blessing into Consolation when Seraph is out.
            if (Configuration.IsEnabled(CustomComboPreset.ScholarSeraphConsolationFeature))
            {
                if (actionID == SCH.FeyBless)
                {
                    if (GetJobGauge<SCHGauge>().SeraphTimer > 0)
                        return SCH.Consolation;
                    return SCH.FeyBless;
                }
            }

            // Change Energy Drain into Aetherflow when you have no more Aetherflow stacks.
            if (Configuration.IsEnabled(CustomComboPreset.ScholarEnergyDrainFeature))
            {
                if (actionID == SCH.EnergyDrain)
                {
                    if (GetJobGauge<SCHGauge>().NumAetherflowStacks == 0)
                        return SCH.Aetherflow;
                    return SCH.EnergyDrain;
                }
            }

            #endregion
            // ====================================================================================
            #region DANCER

            // Fan Dance changes into Fan Dance 3 while flourishing.
            if (Configuration.IsEnabled(CustomComboPreset.DancerFanDanceCombo))
            {
                if (actionID == DNC.FanDance1)
                {
                    if (HasBuff(DNC.Buffs.FlourishingFanDance))
                        return DNC.FanDance3;
                    return DNC.FanDance1;
                }

                // Fan Dance 2 changes into Fan Dance 3 while flourishing.
                if (actionID == DNC.FanDance2)
                {
                    if (HasBuff(DNC.Buffs.FlourishingFanDance))
                        return DNC.FanDance3;
                    return DNC.FanDance2;
                }
            }

            // Standard Step and Technical Steps turn into the movements while dancing.
            if (Configuration.IsEnabled(CustomComboPreset.DancerDanceStepCombo))
            {
                if (actionID == DNC.StandardStep)
                {
                    var gauge = GetJobGauge<DNCGauge>();
                    if (gauge.IsDancing() && HasBuff(DNC.Buffs.StandardStep))
                        if (gauge.NumCompleteSteps < 2)
                            return gauge.NextStep();
                        else
                            return DNC.StandardFinish2;
                }
                if (actionID == DNC.TechnicalStep)
                {
                    var gauge = GetJobGauge<DNCGauge>();
                    if (gauge.IsDancing() && HasBuff(DNC.Buffs.TechnicalStep))
                        if (gauge.NumCompleteSteps < 4)
                            return gauge.NextStep();
                        else
                            return DNC.TechnicalFinish4;
                }
            }

            // Before using Flourish, use any procs
            if (Configuration.IsEnabled(CustomComboPreset.DancerFlourishFeature))
            {
                if (actionID == DNC.Flourish)
                {
                    if (HasBuff(DNC.Buffs.FlourishingFountain))
                        return DNC.Fountainfall;
                    if (HasBuff(DNC.Buffs.FlourishingCascade))
                        return DNC.ReverseCascade;
                    if (HasBuff(DNC.Buffs.FlourishingShower))
                        return DNC.Bloodshower;
                    if (HasBuff(DNC.Buffs.FlourishingWindmill))
                        return DNC.RisingWindmill;
                    return DNC.Flourish;
                }
            }

            // Single target multibutton
            if (Configuration.IsEnabled(CustomComboPreset.DancerSingleTargetMultibutton))
            {
                if (actionID == DNC.Cascade)
                {
                    // From Fountain
                    if (HasBuff(DNC.Buffs.FlourishingFountain))
                        return DNC.Fountainfall;
                    // From Cascade
                    if (HasBuff(DNC.Buffs.FlourishingCascade))
                        return DNC.ReverseCascade;
                    // Cascade Combo
                    if (lastMove == DNC.Cascade && level >= DNC.Levels.Fountain)
                        return DNC.Fountain;
                    return DNC.Cascade;
                }
            }

            // AoE multibutton
            if (Configuration.IsEnabled(CustomComboPreset.DancerAoeMultibutton))
            {
                if (actionID == DNC.Windmill)
                {
                    // From Bladeshower
                    if (HasBuff(DNC.Buffs.FlourishingShower))
                        return DNC.Bloodshower;
                    // From Windmill
                    if (HasBuff(DNC.Buffs.FlourishingWindmill))
                        return DNC.RisingWindmill;
                    // Windmill Combo
                    if (lastMove == DNC.Windmill && level >= DNC.Levels.Bladeshower)
                        return DNC.Bladeshower;
                    return DNC.Windmill;
                }
            }

            #endregion
            // ====================================================================================
            #region WHITE MAGE

            // Replace Solace with Misery when full blood lily
            if (Configuration.IsEnabled(CustomComboPreset.WhiteMageSolaceMiseryFeature))
            {
                if (actionID == WHM.AfflatusSolace)
                {
                    if (GetJobGauge<WHMGauge>().NumBloodLily == 3)
                        return WHM.AfflatusMisery;
                    return WHM.AfflatusSolace;
                }
            }

            // Replace Solace with Misery when full blood lily
            if (Configuration.IsEnabled(CustomComboPreset.WhiteMageRaptureMiseryFeature))
            {
                if (actionID == WHM.AfflatusRapture)
                {
                    if (GetJobGauge<WHMGauge>().NumBloodLily == 3)
                        return WHM.AfflatusMisery;
                    return WHM.AfflatusRapture;
                }
            }

            #endregion
            // ====================================================================================
            #region BARD

            // Replace Wanderer's Minuet with PP when in WM.
            if (Configuration.IsEnabled(CustomComboPreset.BardWanderersPitchPerfectFeature))
            {
                if (actionID == BRD.WanderersMinuet)
                {
                    if (GetJobGauge<BRDGauge>().ActiveSong == CurrentSong.WANDERER)
                        return BRD.PitchPerfect;
                    return BRD.WanderersMinuet;
                }
            }

            // Replace HS/BS with SS/RA when procced.
            if (Configuration.IsEnabled(CustomComboPreset.BardStraightShotUpgradeFeature))
            {
                if (actionID == BRD.HeavyShot || actionID == BRD.BurstShot)
                {
                    if (HasBuff(BRD.Buffs.StraightShotReady))
                    {
                        if (level >= BRD.Levels.RefulgentArrow)
                            return BRD.RefulgentArrow;
                        return BRD.StraightShot;
                    }

                    if (level >= BRD.Levels.BurstShot)
                        return BRD.BurstShot;
                    return BRD.HeavyShot;
                }
            }

            if (Configuration.IsEnabled(CustomComboPreset.BardIronJawsFeature))
            {
                if (actionID == BRD.IronJaws)
                {
                    if (level < BRD.Levels.IronJaws)
                    {
                        var venomous = FindTargetBuff(BRD.Debuffs.VenomousBite);
                        var windbite = FindTargetBuff(BRD.Debuffs.Windbite);
                        if (venomous.HasValue && windbite.HasValue)
                        {
                            if (venomous?.Duration < windbite?.Duration)
                                return BRD.VenomousBite;
                            return BRD.Windbite;
                        }
                        else if (windbite.HasValue || level < BRD.Levels.Windbite)
                            return BRD.VenomousBite;
                        return BRD.Windbite;
                    }
                    if (level < BRD.Levels.BiteUpgrade)
                    {
                        var venomous = TargetHasBuff(BRD.Debuffs.VenomousBite);
                        var windbite = TargetHasBuff(BRD.Debuffs.Windbite);
                        if (venomous && windbite)
                            return BRD.IronJaws;
                        if (windbite)
                            return BRD.VenomousBite;
                        return BRD.Windbite;
                    }
                    var caustic = TargetHasBuff(BRD.Debuffs.CausticBite);
                    var stormbite = TargetHasBuff(BRD.Debuffs.Stormbite);
                    if (caustic && stormbite)
                        return BRD.IronJaws;
                    if (stormbite)
                        return BRD.CausticBite;
                    return BRD.Stormbite;
                }
            }

            #endregion
            // ====================================================================================
            #region MONK

            if (Configuration.IsEnabled(CustomComboPreset.MnkAoECombo))
            {
                if (actionID == MNK.Rockbreaker)
                {
                    if (HasBuff(MNK.Buffs.PerfectBalance) || HasBuff(MNK.Buffs.FormlessFist))
                        return MNK.Rockbreaker;
                    if (HasBuff(MNK.Buffs.OpoOpoForm))
                        return MNK.ArmOfTheDestroyer;
                    if (HasBuff(MNK.Buffs.RaptorForm) && level >= MNK.Levels.FourPointFury)
                        return MNK.FourPointFury;
                    if (HasBuff(MNK.Buffs.CoerlForm) && level >= MNK.Levels.Rockbreaker)
                        return MNK.Rockbreaker;
                    return MNK.ArmOfTheDestroyer;
                }
            }

            #endregion
            // ====================================================================================
            #region RED MAGE

            if (Configuration.IsEnabled(CustomComboPreset.RedMageAoECombo))
            {
                if (actionID == RDM.Veraero2)
                {
                    if (HasBuff(DoM.Buffs.Swiftcast) || HasBuff(RDM.Buffs.Dualcast))
                    {
                        if (level >= RDM.Levels.Impact)
                            return RDM.Impact;
                        return RDM.Scatter;
                    }
                    return RDM.Veraero2;
                }

                if (actionID == RDM.Verthunder2)
                {
                    if (HasBuff(DoM.Buffs.Swiftcast) || HasBuff(RDM.Buffs.Dualcast))
                    {
                        if (level >= RDM.Levels.Impact)
                            return RDM.Impact;
                        return RDM.Scatter;
                    }
                    return RDM.Verthunder2;
                }
            }

            if (Configuration.IsEnabled(CustomComboPreset.RedMageMeleeCombo))
            {
                if (actionID == RDM.Redoublement)
                {
                    var gauge = GetJobGauge<RDMGauge>();
                    if ((lastMove == RDM.Riposte || lastMove == RDM.EnchantedRiposte) && level >= RDM.Levels.Zwerchhau)
                    {
                        if (gauge.BlackGauge >= 25 && gauge.WhiteGauge >= 25)
                            return RDM.EnchantedZwerchhau;
                        return RDM.Zwerchhau;
                    }

                    if (lastMove == RDM.Zwerchhau && level >= RDM.Levels.Redoublement)
                    {
                        if (gauge.BlackGauge >= 25 && gauge.WhiteGauge >= 25)
                            return RDM.EnchantedRedoublement;
                        return RDM.Redoublement;
                    }

                    if (gauge.BlackGauge >= 30 && gauge.WhiteGauge >= 30)
                        return RDM.EnchantedRiposte;
                    return RDM.Riposte;
                }
            }

            if (Configuration.IsEnabled(CustomComboPreset.RedMageVerprocCombo))
            {
                if (actionID == RDM.Verstone)
                {
                    if (level >= RDM.Levels.Scorch && (lastMove == RDM.Verflare || lastMove == RDM.Verholy))
                        return RDM.Scorch;
                    if (HasBuff(RDM.Buffs.VerstoneReady))
                        return RDM.Verstone;
                    if (level >= RDM.Levels.Jolt2)
                        return RDM.Jolt2;
                    return RDM.Jolt;
                }
                if (actionID == RDM.Verfire)
                {
                    if (level >= RDM.Levels.Scorch && (lastMove == RDM.Verflare || lastMove == RDM.Verholy))
                        return RDM.Scorch;
                    if (HasBuff(RDM.Buffs.VerfireReady))
                        return RDM.Verfire;
                    if (level >= RDM.Levels.Jolt2)
                        return RDM.Jolt2;
                    return RDM.Jolt;
                }
            }

            #endregion
            // ====================================================================================

            return GetIconHook.Original(self, actionID);
        }

        #region BuffArray

        private bool HasBuff(short effectId) => FindBuff(effectId) != null;

        private bool TargetHasBuff(short effectId) => FindTargetBuff(effectId) != null;

        private Dalamud.Game.ClientState.Structs.StatusEffect? FindBuff(short effectId) => FindBuff(effectId, ClientState.LocalPlayer, null);

        private Dalamud.Game.ClientState.Structs.StatusEffect? FindTargetBuff(short effectId) => FindBuff(effectId, ClientState.Targets.CurrentTarget, ClientState.LocalPlayer?.ActorId);

        private Dalamud.Game.ClientState.Structs.StatusEffect? FindBuff(short effectId, Dalamud.Game.ClientState.Actors.Types.Actor actor, int? ownerId)
        {
            if (actor == null)
                return null;

            foreach (var status in actor.StatusEffects)
            {
                if (status.EffectId == effectId)
                    if (!ownerId.HasValue || status.OwnerId == ownerId)
                        return status;
            }

            return null;
        }

        /*
        private IntPtr ActiveBuffArray = IntPtr.Zero;
        private unsafe delegate int* GetBuffArray(long* address);

        private bool HasBuff(params short[] needle)
        {
            if (ActiveBuffArray == IntPtr.Zero)
                return false;

            for (var i = 0; i < 60; i++)
                if (needle.Contains(Marshal.ReadInt16(ActiveBuffArray + (12 * i))))
                    return true;
            return false;
        }

        private void UpdateBuffAddress()
        {
            try
            {
                ActiveBuffArray = FindBuffAddress();
            }
            catch (Exception)
            {
                ActiveBuffArray = IntPtr.Zero;
            }
        }

        private unsafe IntPtr FindBuffAddress()
        {
            var num = Marshal.ReadIntPtr(Address.BuffVTableAddr);
            var step2 = (IntPtr)(Marshal.ReadInt64(num) + 0x280);
            var step3 = Marshal.ReadIntPtr(step2);
            var callback = Marshal.GetDelegateForFunctionPointer<GetBuffArray>(step3);
            return (IntPtr)callback((long*)num) + 8;
        }
        */

        #endregion
    }
}
