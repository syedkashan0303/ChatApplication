using Microsoft.Data.SqlClient;
using System.Data;

namespace SignalRMVC.CustomClasses
{
    public class DatabaseJobService
    {
        private readonly IConfiguration _configuration;

        public DatabaseJobService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void RunStoredProcedure()
        {
            string connectionString = _configuration.GetConnectionString("AppDbContextConnection");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                using (SqlCommand cmd = new SqlCommand("DeleteOldReadMappingRecord", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }

}
