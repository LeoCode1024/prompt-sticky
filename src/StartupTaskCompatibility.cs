using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PromptStickyTool
{
    // Older/external logon tasks inherit Windows' battery restrictions. The built-in
    // registry Run entry has no such restrictions and needs no migration.
    internal static class StartupTaskCompatibility
    {
        public static void RepairBatterySettings(string executablePath)
        {
            object serviceObject = null;
            object rootObject = null;
            try
            {
                string fullPath = Path.GetFullPath(executablePath);
                string userSid;
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    userSid = identity.User.Value;
                }

                serviceObject = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
                dynamic service = serviceObject;
                service.Connect();
                rootObject = service.GetFolder(@"\");
                RepairFolder(rootObject, fullPath, userSid);
            }
            catch (Exception ex)
            {
                // A missing service or insufficient permissions must never prevent startup.
                Diagnostics.LogException("StartupTaskBatterySettings", ex);
            }
            finally
            {
                Release(rootObject);
                Release(serviceObject);
            }
        }

        private static void RepairFolder(object folderObject, string executablePath, string userSid)
        {
            object tasksObject = null;
            object foldersObject = null;
            try
            {
                dynamic folder = folderObject;
                tasksObject = folder.GetTasks(1); // Include hidden startup tasks.
                dynamic tasks = tasksObject;
                for (int i = 1; i <= (int)tasks.Count; i++)
                {
                    object taskObject = null;
                    try
                    {
                        taskObject = tasks[i];
                        RepairTask(folderObject, taskObject, executablePath, userSid);
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.LogException("StartupTaskBatterySettings", ex);
                    }
                    finally
                    {
                        Release(taskObject);
                    }
                }

                foldersObject = folder.GetFolders(0);
                dynamic folders = foldersObject;
                for (int i = 1; i <= (int)folders.Count; i++)
                {
                    object childObject = null;
                    try
                    {
                        childObject = folders[i];
                        RepairFolder(childObject, executablePath, userSid);
                    }
                    finally
                    {
                        Release(childObject);
                    }
                }
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("StartupTaskBatterySettings", ex);
            }
            finally
            {
                Release(foldersObject);
                Release(tasksObject);
            }
        }

        private static void RepairTask(object folderObject, object taskObject, string executablePath, string userSid)
        {
            object definitionObject = null;
            object actionsObject = null;
            object actionObject = null;
            object principalObject = null;
            object settingsObject = null;
            object registeredObject = null;
            try
            {
                dynamic task = taskObject;
                definitionObject = task.Definition;
                dynamic definition = definitionObject;
                actionsObject = definition.Actions;
                dynamic actions = actionsObject;
                // A shared task can run other applications; do not change its policy.
                if ((int)actions.Count != 1) return;
                actionObject = actions[1];
                dynamic action = actionObject;
                if ((int)action.Type != 0) return; // TASK_ACTION_EXEC
                string actionPath = Environment.ExpandEnvironmentVariables((string)action.Path).Trim().Trim('"');
                if (!Path.IsPathRooted(actionPath) ||
                    !String.Equals(Path.GetFullPath(actionPath), executablePath, StringComparison.OrdinalIgnoreCase)) return;

                principalObject = definition.Principal;
                dynamic principal = principalObject;
                // Interactive tasks for this user can be updated without credentials.
                if ((int)principal.LogonType != 3 || !IsCurrentUser((string)principal.UserId, userSid)) return;

                settingsObject = definition.Settings;
                dynamic settings = settingsObject;
                if (!(bool)settings.DisallowStartIfOnBatteries && !(bool)settings.StopIfGoingOnBatteries) return;
                settings.DisallowStartIfOnBatteries = false;
                settings.StopIfGoingOnBatteries = false;

                dynamic folder = folderObject;
                // TASK_UPDATE | TASK_IGNORE_REGISTRATION_TRIGGERS: preserve the task and
                // avoid launching another instance while updating its settings.
                registeredObject = folder.RegisterTaskDefinition((string)task.Name, definitionObject,
                    4 | 32, null, null, 3, null);
                Diagnostics.LogMessage("StartupTaskBatterySettings", "已移除便签启动任务的电池供电限制：" + (string)task.Path);
            }
            finally
            {
                Release(registeredObject);
                Release(settingsObject);
                Release(principalObject);
                Release(actionObject);
                Release(actionsObject);
                Release(definitionObject);
            }
        }

        private static bool IsCurrentUser(string account, string userSid)
        {
            if (String.Equals(account, userSid, StringComparison.OrdinalIgnoreCase)) return true;
            if (String.IsNullOrEmpty(account)) return false;
            try
            {
                return new NTAccount(account).Translate(typeof(SecurityIdentifier)).Value == userSid;
            }
            catch (IdentityNotMappedException)
            {
                return false;
            }
        }

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }
}
