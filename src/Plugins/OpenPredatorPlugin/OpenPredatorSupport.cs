using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using IFEOPredatorPatcher.Bootstrapper;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace OpenPredatorPlugin
{
    public class OpenPredatorSupport
    {
        private static bool _notified = false;

        public static void Patch(AssemblyDefinition assembly, PluginContext context)
        {
            context.Log($"[OpenPredator] Host Process ID : {context.ProcessId}");
            context.Log($"[OpenPredator] Target Binary   : {context.TargetPath}");
            context.Log($"[OpenPredator] Working Dir     : {context.TargetDirectory}");

            var module = assembly.MainModule;

            var startupType = module.Types.FirstOrDefault(t => t.Name == "Startup");
            var loadingPageType = module.Types.FirstOrDefault(t => t.Name == "LoadingPage");

            if (startupType == null && loadingPageType == null)
            {
                context.Log("[OpenPredator] Target does not appear to be NitroSense or PredatorSense. Skipping.");
                return;
            }

            // 1. Inject helper method in target assembly that checks services and notifies
            var checkHelperMethod = InjectServiceCheckHelper(module);

            // 2. Patch EnsureProcessesAlive in Startup
            if (startupType != null)
            {
                PatchEnsureProcessesAlive(startupType, checkHelperMethod, context);
            }

            // 3. Patch LoadingPage.SvcCheckTimer_Tick
            if (loadingPageType != null)
            {
                PatchLoadingPage(loadingPageType, checkHelperMethod, context);
            }

            context.Log("[OpenPredator] ✔ OpenPredator dual-service support patched successfully!");
        }

        public static void ShowNotification(string title, string message)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    using (var notifyIcon = new NotifyIcon())
                    {
                        notifyIcon.Icon = SystemIcons.Information;
                        notifyIcon.Visible = true;
                        notifyIcon.BalloonTipTitle = title;
                        notifyIcon.BalloonTipText = message;
                        notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                        notifyIcon.ShowBalloonTip(4000);

                        Thread.Sleep(4500);
                        notifyIcon.Visible = false;
                    }
                }
                catch
                {
                    // Non-blocking notification fallback
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private static MethodDefinition InjectServiceCheckHelper(ModuleDefinition module)
        {
            var helperType = module.Types.FirstOrDefault(t => t.Name == "<OpenPredatorHookHelper>");
            if (helperType == null)
            {
                helperType = new TypeDefinition(
                    "",
                    "<OpenPredatorHookHelper>",
                    TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                    module.TypeSystem.Object
                );
                module.Types.Add(helperType);
            }

            var method = helperType.Methods.FirstOrDefault(m => m.Name == "IsBackendServiceAlive");
            if (method != null) return method;

            method = new MethodDefinition(
                "IsBackendServiceAlive",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                module.TypeSystem.Boolean
            );

            var getProcessesByNameMethod = module.ImportReference(
                typeof(Process).GetMethod("GetProcessesByName", new[] { typeof(string) })
            );
            var notifyMethod = module.ImportReference(
                typeof(OpenPredatorSupport).GetMethod(nameof(OnServiceChecked), new[] { typeof(bool), typeof(bool) })
            );

            var il = method.Body.GetILProcessor();

            var lblCheckOpenPredatorService = Instruction.Create(OpCodes.Nop);
            var lblCheckPSSvc = Instruction.Create(OpCodes.Nop);
            var lblReturnOpenPredator = Instruction.Create(OpCodes.Nop);
            var lblReturnPSSvc = Instruction.Create(OpCodes.Nop);

            // 1. Check Process: "OpenPredator"
            il.Emit(OpCodes.Ldstr, "OpenPredator");
            il.Emit(OpCodes.Call, getProcessesByNameMethod);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Brtrue_S, lblReturnOpenPredator);

            // 2. Check Process: "OpenPredatorService"
            il.Append(lblCheckOpenPredatorService);
            il.Emit(OpCodes.Ldstr, "OpenPredatorService");
            il.Emit(OpCodes.Call, getProcessesByNameMethod);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Brtrue_S, lblReturnOpenPredator);

            // 3. Check Process: "PSSvc"
            il.Append(lblCheckPSSvc);
            il.Emit(OpCodes.Ldstr, "PSSvc");
            il.Emit(OpCodes.Call, getProcessesByNameMethod);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Brtrue_S, lblReturnPSSvc);

            // Default fallback -> OpenPredator
            il.Append(lblReturnOpenPredator);
            il.Emit(OpCodes.Ldc_I4_1); // isOpenPredator = true
            il.Emit(OpCodes.Ldc_I4_0); // isPSSvc = false
            il.Emit(OpCodes.Call, notifyMethod);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);

            // PSSvc
            il.Append(lblReturnPSSvc);
            il.Emit(OpCodes.Ldc_I4_0); // isOpenPredator = false
            il.Emit(OpCodes.Ldc_I4_1); // isPSSvc = true
            il.Emit(OpCodes.Call, notifyMethod);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);

            helperType.Methods.Add(method);
            return method;
        }

        public static void OnServiceChecked(bool isOpenPredator, bool isPSSvc)
        {
            if (_notified) return;
            _notified = true;

            if (isOpenPredator)
            {
                ShowNotification(
                    "OpenPredator Active",
                    "Sense application connected to OpenPredator backend service pipe!"
                );
            }
            else if (isPSSvc)
            {
                ShowNotification(
                    "Stock PSSvc Active",
                    "Sense application connected to stock Acer PSSvc backend service."
                );
            }
        }

        private static void PatchEnsureProcessesAlive(TypeDefinition startupType, MethodDefinition checkHelperMethod, PluginContext context)
        {
            var method = startupType.Methods.FirstOrDefault(m => m.Name == "EnsureProcessesAlive");
            if (method == null || !method.HasBody) return;

            var il = method.Body.GetILProcessor();
            var firstInstr = method.Body.Instructions[0];

            var lblContinueStock = Instruction.Create(OpCodes.Nop);

            il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Call, checkHelperMethod));
            il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Brfalse_S, lblContinueStock));
            il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Ret));
            il.InsertBefore(firstInstr, lblContinueStock);

            context.Log("[OpenPredatorSupport] ✔ Patched Startup.EnsureProcessesAlive");
        }

        private static void PatchLoadingPage(TypeDefinition loadingPageType, MethodDefinition checkHelperMethod, PluginContext context)
        {
            var tickMethod = loadingPageType.Methods.FirstOrDefault(m => m.Name == "SvcCheckTimer_Tick");
            if (tickMethod == null || !tickMethod.HasBody) return;

            var svcExistField = loadingPageType.Fields.FirstOrDefault(f => f.Name == "_svc_exist");
            var acModeCheckField = loadingPageType.Fields.FirstOrDefault(f => f.Name == "_ac_mode_check");
            var batteryBoostCheckField = loadingPageType.Fields.FirstOrDefault(f => f.Name == "_battery_boost_check");

            if (svcExistField == null) return;

            var il = tickMethod.Body.GetILProcessor();
            var firstInstr = tickMethod.Body.Instructions[0];

            var lblSkip = Instruction.Create(OpCodes.Nop);

            // Injects at start of SvcCheckTimer_Tick:
            // if (IsBackendServiceAlive()) {
            //     this._svc_exist = true;
            //     this._ac_mode_check = true;
            //     this._battery_boost_check = true;
            // }
            il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Call, checkHelperMethod));
            il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Brfalse_S, lblSkip));

            il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Ldc_I4_1));
            il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Stfld, svcExistField));

            if (acModeCheckField != null)
            {
                il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Ldc_I4_1));
                il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Stfld, acModeCheckField));
            }

            if (batteryBoostCheckField != null)
            {
                il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Ldc_I4_1));
                il.InsertBefore(firstInstr, Instruction.Create(OpCodes.Stfld, batteryBoostCheckField));
            }

            il.InsertBefore(firstInstr, lblSkip);

            context.Log("[OpenPredatorSupport] ✔ Patched LoadingPage.SvcCheckTimer_Tick");
        }
    }
}
