﻿using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

namespace BeltsAndSortersQOL
{
    [BepInPlugin(__GUID__, __NAME__, "1.0.0")]
    public class BeltsAndSortersQOL : BaseUnityPlugin
    {
        public const string __NAME__ = "BeltsAndSortersQOL";
        public const string __GUID__ = "com.Trol1face.dsp." + __NAME__;
        public static ConfigEntry<bool> holdReleaseBeltsBuilding;
        public static ConfigEntry<bool> holdReleaseSortersBuilding;
        public static ConfigEntry<bool> disableBeltProlongation;
        public static ConfigEntry<bool> altitudeValueInCursorText;
        public static ConfigEntry<bool> shortVerOfAltitudeAndLength;
        public static ConfigEntry<bool> autoTakeBeltsAltitude;

        private void Awake()
        {
            // Plugin startup logic
            //Logger.LogInfo($"Plugin {__GUID__} is loaded!");
            holdReleaseBeltsBuilding = Config.Bind("General", "holdReleaseBeltsBuilding", true,
                "Enable 1 click building for Belts");
            holdReleaseSortersBuilding = Config.Bind("General", "holdReleaseSortersBuilding", true,
                "Enable 1 click building for Sorters");
            disableBeltProlongation = Config.Bind("General", "disableBeltProlongation", true,
                "If set on TRUE ending belt on ground will not start another belt in the end of builded one. In vanilla if you build end a belt into nothing, end of the belt becomes a new start and you continue to build it or cancel with RMB. This feature disables that");
            altitudeValueInCursorText = Config.Bind("General", "AltitudeValueInCursorText", true,
                "There will be a text near cursor representing current belt's altitude (instead of tips about clicking to build)");
            shortVerOfAltitudeAndLength = Config.Bind("General", "ShortVerOfAltitudeAndLength", false,
                "Enable this in addition to previous config to change form from *Altitude: n/Length: n* to short version *A: n| L: n");
            autoTakeBeltsAltitude = Config.Bind("General", "autoTakeBeltsAltitude", true,
                "If you start a belt in another belt your current altitude will change to this belt's altitude automaticly");
            
            new Harmony(__GUID__).PatchAll(typeof(Patch));
        }

        static class Patch
        {
            [HarmonyTranspiler, HarmonyPatch(typeof(BuildTool_Path), "ConfirmOperation")]
            public static IEnumerable<CodeInstruction> BuildTool_Path_ConfirmOperation_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilgen)
            {
                if (holdReleaseBeltsBuilding.Value || autoTakeBeltsAltitude.Value) 
                {
                    CodeMatcher matcher = new(instructions);
                    if (holdReleaseBeltsBuilding.Value)
                    {
                        Label falseLabel = ilgen.DefineLabel();//jump here if condition is false, to the end of method
                        Label continueLabel = ilgen.DefineLabel();//skip onUp && waitForConfirm if onDown is true
                        List<Label> falseLabelList = new();//matcher adds only IEnumerables of labels
                        List<Label> continueLabelList = new();
                        FieldInfo insertAnchor = typeof(VFInput.InputValue).GetField("onDown");
                        falseLabelList.Add(falseLabel);
                        continueLabelList.Add(continueLabel);
                        /*
                        find
                            call      valuetype VFInput/InputValue VFInput::get__buildConfirm()
                            ldfld     bool VFInput/InputValue::onDown
                            brfalse   IL_00C7
                            call      void VFInput::UseMouseLeft()
                        */
                        Debug.Log("....Matching for insert started. Matcher pos " + matcher.Pos);
                        matcher.MatchForward(true, new CodeMatch(i => i.opcode == OpCodes.Ldfld && i.operand is FieldInfo f && f == insertAnchor));
                        if (matcher.Pos != -1) {
                            Debug.Log("....Searching onDown. Found " + matcher.Instruction.ToString());
                            /*
                                four lines i'm using. Deleting first 3, and changing with my own condition. 
                                continueLabel is added to UseMouseLeft() method
                                falseLabel is added to ldc.i4.0 in the end of method
                            */
                            

                            //target and delete the condition.
                            matcher.Advance(-1);//current pos is ldfld onDown, i'm deleting call before it too
                            matcher.RemoveInstructions(3);
                            //put our own condition
                            matcher.InsertAndAdvance(
                                new CodeInstruction(OpCodes.Call, typeof(VFInput).GetMethod("get__buildConfirm")),
                                new CodeInstruction(OpCodes.Ldfld, typeof(VFInput.InputValue).GetField("onDown")),
                                new CodeInstruction(OpCodes.Brtrue, continueLabel),
                                new CodeInstruction(OpCodes.Call, typeof(VFInput).GetMethod("get__buildConfirm")),
                                new CodeInstruction(OpCodes.Ldfld, typeof(VFInput.InputValue).GetField("onUp")),
                                new CodeInstruction(OpCodes.Brfalse, falseLabel),
                                new CodeInstruction(OpCodes.Ldarg_0),
                                new CodeInstruction(OpCodes.Ldfld, typeof(BuildTool_Path).GetField("waitForConfirm")),
                                new CodeInstruction(OpCodes.Brfalse, falseLabel)
                            );
                            matcher.AddLabels(continueLabelList);
                            matcher.End();
                            Debug.Log("END pos is" + matcher.Pos);
                            /*
                            find
                                ...
                                ldc.i4.0 <--   at the end of the method
                                ret
                            */
                            matcher.MatchBack(true, new CodeMatch(i => i.opcode == OpCodes.Ldc_I4_0));
                            Debug.Log("....Searching for LDC_I4_0. Found " + matcher.Instruction.ToString());
                            matcher.AddLabels(falseLabelList);
                            
                            foreach (CodeInstruction ins in matcher.Instructions()) Debug.Log(".. " + ins.ToString());

                        }
                    }
                    return matcher.InstructionEnumeration();
                }
                return instructions;
            }

            [HarmonyTranspiler, HarmonyPatch(typeof(BuildTool_Inserter), "ConfirmOperation")]
            public static IEnumerable<CodeInstruction> BuildTool_Inserter_ConfirmOperation_Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                if (holdReleaseSortersBuilding.Value)
                {
                    FieldInfo anchor = typeof(VFInput.InputValue).GetField("onDown");
                    FieldInfo rep = typeof(VFInput.InputValue).GetField("onUp");
                    int insertIndex = -1;
                    // Grab all the instructions
                    var codes = new List<CodeInstruction>(instructions);
                    for(int i = 0; i < codes.Count; i++)
                    {
                        if(codes[i].opcode == OpCodes.Ldfld && codes[i].operand is FieldInfo o && o == anchor)
                        {
                        //Debug.Log(" insertIndex detected in this line: " + i);
                            insertIndex = i;
                            break;

                        }
                        //Debug.Log("");
                    }
                    if (insertIndex > -1)
                    {
                        codes[insertIndex].operand = rep;
                    }
                    return codes.AsEnumerable();
                }
                return instructions;
            }
            
            [HarmonyTranspiler, HarmonyPatch(typeof(BuildTool_Path), "CreatePrebuilds")]
            public static IEnumerable<CodeInstruction> BuildTool_Path_CreatePrebuilds_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilgen)
            {
                if (disableBeltProlongation.Value)
                {
                    Label jump = ilgen.DefineLabel();
                    List<Label> labelList = new();//matcher adds only IEnumerables of labels
                    CodeInstruction jumpPoint = new(OpCodes.Br, jump);
                    CodeMatcher matcher = new(instructions);
                    MethodInfo anchor = typeof(BuildTool).GetMethod("get_buildPreviews");
                    labelList.Add(jump);
                    /*
                    find
                    ..ldarg.0
                    ..ldarg.0
                    ..call instance class [netstandard]System.Collections.Generic.List`1<class BuildPreview> BuildTool::get_buildPreviews()
                    ..call instance void BuildTool_Path::AddUpBuildingPathLength(class [netstandard]System.Collections.Generic.List`1<class BuildPreview>)
                    */
                    matcher.MatchForward(false,
                        new CodeMatch(i => i.opcode == OpCodes.Ldarg_0),
                        new CodeMatch(i => i.opcode == OpCodes.Ldarg_0),
                        new CodeMatch(i => i.opcode == OpCodes.Call && i.operand is MethodInfo m && m == anchor),
                        new CodeMatch(i => i.opcode == OpCodes.Call)
                    );
                    if (matcher.Pos != -1) 
                    {
                        //Debug.Log("...Found destination point here " + matcher.Pos);
                        matcher.AddLabels(labelList);
                        matcher.Start();
                        /*
                        find
                        ..ble       IL_07BF
                            WILL ADD JUMP HERE
                        ..ldc.i4.1
                        ..stloc.s   V_32
                        ..ldarg.0
                        ..call instance class [netstandard]System.Collections.Generic.List`1<class BuildPreview> BuildTool::get_buildPreviews()
                        */
                        matcher.MatchForward(false,
                        new CodeMatch(i => i.opcode == OpCodes.Ble),// only for matcher, jump point is the next one
                        new CodeMatch(i => i.opcode == OpCodes.Ldc_I4_1),//need to place jump before this instruction
                        new CodeMatch(i => i.opcode == OpCodes.Stloc_S),
                        new CodeMatch(i => i.opcode == OpCodes.Ldarg_0),
                        new CodeMatch(i => i.opcode == OpCodes.Call  && i.operand is MethodInfo m && m == anchor)
                        );
                        if (matcher.Pos != 0) {
                            //Debug.Log("...Found jump point here " + matcher.Pos + 1);
                            matcher.Advance(1);//moving from Ble
                            matcher.Insert(jumpPoint);
                            //Debug.Log("..Inserted jump " + jumpPoint.ToString());
                            //foreach (CodeInstruction ins in matcher.Instructions()) Debug.Log(".. " + ins.ToString());
                            return matcher.InstructionEnumeration();
                        }
                    }
                }
                return instructions;
            }

            //Adding altitude value to cursor text when building you take belt in hands
            [HarmonyTranspiler, HarmonyPatch(typeof(BuildTool_Path), "DeterminePreviews")]
            public static object BuildTool_Path_DeterminePreviews_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilgen)
            {
                if(altitudeValueInCursorText.Value) 
                {
                    CodeMatcher matcher = new(instructions);
                    MethodInfo rep = typeof(BeltsAndSortersQOL).GetMethod("CursorText_DeterminePreviews");
                    MethodInfo repShort = typeof(BeltsAndSortersQOL).GetMethod("ShortCursorText_DeterminePreviews");
                    matcher.MatchForward(true, new CodeMatch(i => i.opcode == OpCodes.Ldstr && (String)i.operand == "选择起始位置"));
                    if (matcher.Pos != -1) 
                    {
                        matcher.RemoveInstructions(2);
                        if (shortVerOfAltitudeAndLength.Value) {
                            matcher.Insert(new CodeInstruction(OpCodes.Call, repShort));
                        } else {
                            matcher.Insert(new CodeInstruction(OpCodes.Call, rep));
                        }
                        //Log to see what we changed
                        //foreach (CodeInstruction ins in matcher.InstructionsInRange(matcher.Pos - 3, matcher.Pos + 10)) Debug.Log("........... " + ins.ToString());
                    }
                    return matcher.InstructionEnumeration();
                }
                return instructions;
            }
            
            //Adding altitude value & length of the belt to cursor text when you started belt building
            [HarmonyTranspiler, HarmonyPatch(typeof(BuildTool_Path), "CheckBuildConditions")]
            public static object BuildTool_Path_CheckBuildConditions_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilgen)
            {
                if(altitudeValueInCursorText.Value) 
                {
                    CodeMatcher matcher = new(instructions);
                    MethodInfo rep = typeof(BeltsAndSortersQOL).GetMethod("CursorText_CheckBuildConditions");
                    MethodInfo repShort = typeof(BeltsAndSortersQOL).GetMethod("ShortCursorText_CheckBuildConditions");
                    matcher.MatchForward(true, new CodeMatch(i => i.opcode == OpCodes.Ldstr && (String)i.operand == "点击鼠标建造"));
                    if (matcher.Pos != -1) 
                    {
                        matcher.RemoveInstructions(8);
                        if (shortVerOfAltitudeAndLength.Value) {
                            matcher.Insert(new CodeInstruction(OpCodes.Call, repShort));
                        } else {
                            matcher.Insert(new CodeInstruction(OpCodes.Call, rep));
                        }
                        //Log to see what we changed
                        //foreach (CodeInstruction ins in matcher.InstructionsInRange(matcher.Pos - 3, matcher.Pos + 10)) Debug.Log("........... " + ins.ToString());
                    }
                    return matcher.InstructionEnumeration();
                }
                return instructions;
            }

        }

        


        public static String CursorText_DeterminePreviews() {
            BuildTool_Path tool = GameMain.mainPlayer.controller.actionBuild.pathTool;
            String altitude = tool.altitude.ToString();
            return "Altitude: " + altitude + System.Environment.NewLine + "Length: 0";
            }
        public static String ShortCursorText_DeterminePreviews() {
            BuildTool_Path tool = GameMain.mainPlayer.controller.actionBuild.pathTool;
            String altitude = tool.altitude.ToString();
            return "A: " + altitude + " | L: 0";
            }
        public static String CursorText_CheckBuildConditions() {
            BuildTool_Path tool = GameMain.mainPlayer.controller.actionBuild.pathTool;
            String altitude = tool.altitude.ToString();
            String length = tool.pathPointCount.ToString();
            return "Altitude: " + altitude + System.Environment.NewLine + "Length: " + length;
        }
        public static String ShortCursorText_CheckBuildConditions() {
            BuildTool_Path tool = GameMain.mainPlayer.controller.actionBuild.pathTool;
            String altitude = tool.altitude.ToString();
            String length = tool.pathPointCount.ToString();
            return "A: " + altitude + " | L: " + length;
        }

    }
}