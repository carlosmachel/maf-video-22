// sample.cs — Arquivo de exemplo para revisão pelo CodeReviewAgent.
// ATENÇÃO: este arquivo contém problemas propositais de segurança e performance.

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SampleApp
{
    // -------------------------------------------------------------------------
    // [BUG 1 — SEGURANÇA] Credenciais hardcoded
    // -------------------------------------------------------------------------
    public static class Config
    {
        public const string ConnectionString =
            "Server=prod-db.empresa.com;Database=Clientes;User Id=sa;Password=Abc@1234!;";

        public const string ApiKey = "sk-prod-xK92mNpLqR7vTwYz3cBdFgHjUeOiAs10";
    }

    // -------------------------------------------------------------------------
    // [BUG 2 — SEGURANÇA] SQL Injection clássico
    // -------------------------------------------------------------------------
    public class CustomerRepository
    {
        public List<string> SearchCustomers(string name)
        {
            var results = new List<string>();

            // Concatenação direta da entrada do usuário na query — SQL Injection.
            string query = "SELECT Nome FROM Clientes WHERE Nome LIKE '%" + name + "%'";

            using var connection = new SqlConnection(Config.ConnectionString);
            connection.Open();

            var command = new SqlCommand(query, connection);
            using var reader = command.ExecuteReader();

            while (reader.Read())
                results.Add(reader.GetString(0));

            return results;
        }

        public void DeleteCustomer(int id)
        {
            // Sem verificação de autorização antes de deletar.
            string query = $"DELETE FROM Clientes WHERE Id = {id}";

            var connection = new SqlConnection(Config.ConnectionString);
            // [BUG 3 — RESOURCE LEAK] SqlConnection não está em bloco using.
            connection.Open();
            new SqlCommand(query, connection).ExecuteNonQuery();
        }
    }

    // -------------------------------------------------------------------------
    // [BUG 4 — SEGURANÇA] Deserialização insegura + path traversal
    // -------------------------------------------------------------------------
    public class ReportService
    {
        private readonly string _baseDir = "C:\\Reports";

        public string ReadReport(string fileName)
        {
            // Path traversal: atacante pode passar "../../windows/system32/..."
            string fullPath = Path.Combine(_baseDir, fileName);
            return File.ReadAllText(fullPath);
        }

        public object LoadConfig(string base64Json)
        {
            // Deserialização de tipo arbitrário sem validação.
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64Json));
            return Newtonsoft.Json.JsonConvert.DeserializeObject(json,
                new Newtonsoft.Json.JsonSerializerSettings
                {
                    TypeNameHandling = Newtonsoft.Json.TypeNameHandling.All  // inseguro
                });
        }
    }

    // -------------------------------------------------------------------------
    // [BUG 5 — PERFORMANCE] HttpClient instanciado por chamada (socket exhaustion)
    // -------------------------------------------------------------------------
    public class NotificationService
    {
        public async Task SendWebhookAsync(string url, string payload)
        {
            // Novo HttpClient a cada chamada — esgota sockets em produção.
            var client = new HttpClient();
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await client.PostAsync(url, content);
            // [BUG 6 — RESOURCE LEAK] HttpClient não é descartado.
        }

        public void NotifyAll(List<string> urls, string message)
        {
            foreach (var url in urls)
            {
                // Chamada async bloqueante — pode causar deadlock em contextos de UI/ASP.NET.
                SendWebhookAsync(url, message).Wait();
            }
        }
    }

    // -------------------------------------------------------------------------
    // [BUG 7 — PERFORMANCE] Concatenação de string em loop (O(n²) de alocações)
    // -------------------------------------------------------------------------
    public class ReportBuilder
    {
        public string BuildCsv(List<string[]> rows)
        {
            string csv = "";  // deveria ser StringBuilder

            foreach (var row in rows)
            {
                foreach (var cell in row)
                    csv += cell + ",";  // nova alocação a cada iteração

                csv += Environment.NewLine;
            }

            return csv;
        }

        // [BUG 8 — PERFORMANCE] LINQ desnecessariamente complexo e com múltiplas
        // enumerações da mesma coleção.
        public int CountActiveAdmins(IEnumerable<User> users)
        {
            return users
                .Where(u => u.IsActive == true)       // == true redundante
                .Where(u => u.Role == "Admin")         // deveria ser combinado com o Where acima
                .Select(u => u)                        // Select identidade inútil
                .ToList()                              // materializa antes de contar
                .Count();                              // Count() no lugar de Count
        }
    }

    // -------------------------------------------------------------------------
    // [BUG 9 — SEGURANÇA] Exposição de exceção interna ao cliente
    // -------------------------------------------------------------------------
    public class OrderController
    {
        private readonly CustomerRepository _repo = new();

        public string ProcessOrder(string customerName, int customerId)
        {
            try
            {
                var customers = _repo.SearchCustomers(customerName);
                if (customers.Count == 0)
                    return "Cliente não encontrado.";

                _repo.DeleteCustomer(customerId);
                return "Pedido processado.";
            }
            catch (Exception ex)
            {
                // Stack trace e mensagem interna retornados diretamente ao cliente.
                return $"Erro: {ex.ToString()}";
            }
        }
    }

    // -------------------------------------------------------------------------
    // [BUG 10 — PERFORMANCE] Operação síncrona bloqueante em método async
    // -------------------------------------------------------------------------
    public class DataLoader
    {
        public async Task<string> LoadDataAsync(string filePath)
        {
            // File.ReadAllText é síncrono — bloqueia a thread do pool async.
            // Deveria ser await File.ReadAllTextAsync(filePath).
            return File.ReadAllText(filePath);
        }

        public async Task<byte[]> DownloadAsync(string url)
        {
            using var client = new HttpClient();
            // GetResult() bloqueia — deveria ser await.
            return client.GetByteArrayAsync(url).Result;
        }
    }

    // Modelo simples usado acima
    public class User
    {
        public string Name { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; }
    }
}