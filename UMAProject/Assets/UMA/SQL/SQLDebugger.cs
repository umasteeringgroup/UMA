using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using UnityEngine;

public class SQLDebugger : MonoBehaviour
{
#if UNITY_EDITOR
    public string Server;
    public string user;
    public string pw;
    public string Database = "UnityLogs";

    public static SQLDebugger Instance;
    public bool ForceInitialize;

    // Start is called before the first frame update
    void Start()
    {
        Instance = this;
        InitializeDebugger();
    }


    private static bool initialized = false;
    private static SqlConnection sqc;
    private static string connectionString;
    private static int runKey;

    private void OnValidate()
    {
        if (ForceInitialize)
        {
            Instance = this;
            InitializeDebugger();
            ForceInitialize = false;
        }
    }

    public static void InitializeDebugger()
    {
        if (Instance == null)
        {
            return;
        }

        if (!initialized)
        {
           // string info = System.DateTime.UtcNow.ToString("dddd, MMMM dd yyyy", System.Globalization.DateTimeFormatInfo.InvariantInfo);

           // runKey = UMA.UMAUtils.StringToHash(info).ToString();

            var sqcb = new SqlConnectionStringBuilder();
            sqcb.UserID = Instance.user;
            sqcb.Password = Instance.pw;
            sqcb.IntegratedSecurity = false;
            sqcb.DataSource = Instance.Server;
            sqcb.InitialCatalog = Instance.Database;
            sqcb.ConnectTimeout = 5;

            connectionString = sqcb.ConnectionString;
            sqc = new SqlConnection(connectionString);


            startRun();

            Application.logMessageReceived += Application_logMessageReceived;
            initialized = true;
        }

    }

    private static int startRun()
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
                    runKey = Convert.ToInt32(cmd.ExecuteScalar());
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
#endif
}
