#if DEBUG_SERIALIZATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;

public static class SQLDebugger 
{
    public const string Server = ".\\SQLEXPRESS";
    public const string Database = "IndexLogging";

    private static SqlConnection sqc;
    private static string connectionString;
    public static bool initialized = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    public static void AfterAssembliesLoaded()
    {
        InitializeDebugger();
    }


    public static void InitializeDebugger()
    {
        initialized = true;
        Debug.Log("Initializing SQL Debugger");
        // string info = System.DateTime.UtcNow.ToString("dddd, MMMM dd yyyy", System.Globalization.DateTimeFormatInfo.InvariantInfo);
        // runKey = UMA.UMAUtils.StringToHash(info).ToString();
        var sqcb = new SqlConnectionStringBuilder();
        sqcb.IntegratedSecurity = true;
        sqcb.DataSource = Server;
        sqcb.InitialCatalog = Database;
        sqcb.ConnectTimeout = 1;
        sqcb.MinPoolSize = 5;
        sqcb.Pooling = true;


        connectionString = sqcb.ConnectionString;
        sqc = new SqlConnection(connectionString);
        //Application.logMessageReceived += Application_logMessageReceived;
    }

    public static SqlConnection GetConnection()
    {
        return sqc;
    }

    public static void StartLogging()
    {
    }

    public static void LogSerialization(string message, string stackTrace, string instanceKey, bool isClear, float time)
    {
        if (!initialized)
        {
            InitializeDebugger();
        }
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            //Debug.Log("Opening connection to SQL Server");
            try
            {
                connection.Open();
                string sql = "INSERT INTO SL(Text,CallStack,IsClear,TimeSinceStartup,InstanceKey) " +
                    "VALUES(@Text,@Callstack,@IsClear,@TimeSinceStartup,@InstanceKey)";
                using (SqlCommand cmd = new SqlCommand(sql, connection))
                {
                    cmd.Parameters.Add("@Text", SqlDbType.NVarChar).Value = message;
                    cmd.Parameters.Add("@Callstack", SqlDbType.NVarChar).Value = stackTrace;
                    cmd.Parameters.Add("@IsClear", SqlDbType.Bit).Value = isClear;
                    cmd.Parameters.Add("@TimeSinceStartup", SqlDbType.Float).Value = time;
                    cmd.Parameters.Add("@InstanceKey", SqlDbType.NVarChar).Value = instanceKey;
                    cmd.CommandType = CommandType.Text;
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Application.logMessageReceived -= Application_logMessageReceived;
                Debug.LogException(ex);
            }
        }
    }

    public static int startRun()
    {
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            try
            {
                connection.Open();

                string sql = "exec VP_AddRun @description,@username,@machinename";
                // string sql = "INSERT INTO logrun(UserName,MachineName) VALUES(@username,@machinename); select SCOPE_IDENTITY()";
                using (SqlCommand cmd = new SqlCommand(sql, connection))
                {
                    cmd.Parameters.Add("@description", SqlDbType.NVarChar).Value = "Unity Log";
                    cmd.Parameters.Add("@username", SqlDbType.NVarChar).Value = Environment.UserName;
                    cmd.Parameters.Add("@machinename", SqlDbType.NVarChar).Value = Environment.MachineName;
                    cmd.CommandType = CommandType.Text;
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            return -1;
        }
    }

    private static void Application_logMessageReceived(string condition, string stackTrace, LogType type)
    {
        int runKey = 0;
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            try
            {
                connection.Open();
                string sql = "INSERT INTO log(Message,CallStack,MessageType,RunKey,FrameCount) VALUES(@message,@callstack,@messagetype,@runkey,@framecount)";
                using (SqlCommand cmd = new SqlCommand(sql, connection))
                {
                    cmd.Parameters.Add("@message", SqlDbType.NVarChar).Value = condition;
                    cmd.Parameters.Add("@callstack", SqlDbType.NVarChar).Value = stackTrace;
                    cmd.Parameters.Add("@messagetype", SqlDbType.NVarChar).Value = type.ToString();
                    cmd.Parameters.Add("@runkey", SqlDbType.Int).Value = runKey;
                    cmd.Parameters.Add("@framecount", SqlDbType.Int).Value = Time.frameCount;
                    cmd.CommandType = CommandType.Text;
                    cmd.ExecuteNonQuery();
                }
            }
            catch(Exception ex)
            {
                Application.logMessageReceived -= Application_logMessageReceived;
                Debug.LogException(ex);
            }
        }
    }
}
#endif
#endif