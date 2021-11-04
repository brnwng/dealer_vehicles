using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Linq;
using System.Collections;
using Newtonsoft.Json;

namespace CoxApp
{
    class Program
    {
        public class Data
        {
            [JsonPropertyName("datasetId")]
            public string DatasetId { get; set; }
        }

        public class VehicleIdListResponse
        {
            [JsonPropertyName("vehicleIds")]
            public int[] Ids { get; set; }
        }

        public class VehicleResponse
        {
            [JsonPropertyName("vehicleId")]
            public int Id { get; set; }

            [JsonPropertyName("year")]
            public int Year { get; set; }

            [JsonPropertyName("make")]
            public string Make { get; set; }

            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("dealerId")]
            public int DealerId { get; set; }
        }

        public class Vehicle
        {
            [JsonPropertyName("vehicleId")]
            public int VehicleId { get; set; }

            [JsonPropertyName("year")]
            public int Year { get; set; }

            [JsonPropertyName("make")]
            public string Make { get; set; }

            [JsonPropertyName("model")]
            public string Model { get; set; }
        }

        public class DealerResponse
        {
            [JsonPropertyName("dealerId")]
            public int DealerId { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }
        }

        public class Answer
        {
            [JsonPropertyName("dealers")]
            public IEnumerable<Dealer> Dealers { get; set; }
        }

        public class Dealer
        {
            [JsonPropertyName("dealerId")]
            public int DealerId { get; set; }
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("vehicles")]
            public IEnumerable<Vehicle> Vehicles { get; set; }
        }

        public class AnswerResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; }

            [JsonPropertyName("totalMilliseconds")]
            public int TotalMilliseconds { get; set; }
        }


        static async Task Main(string[] args)
        {
            await ProcessApp();
        }

        private static async Task ProcessApp()
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri("http://api.coxauto-interview.com/api/");
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var dealerCars = new Dictionary<int, List<Vehicle>>();
                var dealers = new Hashtable();
                var dealerIdHash = new List<int>();

                string datasetId = await GetDatasetIdAsync(client);

                var resVehicles = await client.GetAsync(datasetId + "/vehicles");

                if (resVehicles.IsSuccessStatusCode)
                {
                    var data = await System.Text.Json.JsonSerializer.DeserializeAsync<VehicleIdListResponse>(await resVehicles.Content.ReadAsStreamAsync());

                    Parallel.ForEach(data.Ids.Where(x => x > 0), new ParallelOptions() { MaxDegreeOfParallelism = 3 }, vehicleId =>
                    {
                        var car = GetCarAsync(vehicleId, client, datasetId);

                        if (car != null)
                        {
                            var dealerId = car.DealerId;

                            if (!dealerIdHash.Contains(dealerId))
                            {
                                dealerIdHash.Add(dealerId);
                            }

                            AddToCarDictionary(dealerCars, car, dealerId, vehicleId);
                        }
                    });

                    var dealerTasks = dealerIdHash.Select(dealerId =>
                        client.GetAsync(datasetId + "/dealers/" + dealerId));
                    var totalDealers = await Task.WhenAll(dealerTasks);

                    for (var i = 0; i < totalDealers.Count(); i++)
                    {
                        var dealerString = totalDealers[i].Content.ReadAsStringAsync();
                        var dealerJson = JsonConvert.DeserializeObject<DealerResponse>(dealerString.Result);
                        dealers[dealerJson.DealerId] = dealerJson.Name;
                    }

                    var dealerObject = dealerIdHash.Where(x => x > 0).Select(id =>
                    {
                        return new Dealer
                        {
                            DealerId = id,
                            Name = dealers[id].ToString(),
                            Vehicles = dealerCars[id]
                        };
                    });

                    var answer = new Answer { Dealers = dealerObject };
                    var jsonString = JsonConvert.SerializeObject(answer);

                    var res = await client.PostAsync(datasetId + "/answer", new StringContent(jsonString, System.Text.Encoding.UTF8, "application/json"));
                    var ans = JsonConvert.DeserializeObject<AnswerResponse>(res.Content.ReadAsStringAsync().Result);

                    Console.WriteLine($"{ans.Message} {ans.TotalMilliseconds}");
                }
            }

            static void AddToCarDictionary(Dictionary<int, List<Vehicle>> dealerCars, VehicleResponse car, int dealerId, int vehicleId)
            {
                if (dealerId <= 0)
                {
                    return;
                }

                var newCar = new Vehicle
                {
                    VehicleId = vehicleId,
                    Make = car.Make,
                    Model = car.Model,
                    Year = car.Year
                };

                List<Vehicle> cars;

                if (dealerCars.TryGetValue(dealerId, out cars))
                {
                    cars.Add(newCar);
                }
                else
                {
                    var newCars = new List<Vehicle>();
                    newCars.Add(newCar);

                    dealerCars.Add(dealerId, newCars);
                }
            }

            static async Task<string> GetDatasetIdAsync(HttpClient client)
            {
                var datasetId = "";

                var res = await client.GetAsync("datasetId");

                if (res.IsSuccessStatusCode)
                {
                    var data = await System.Text.Json.JsonSerializer.DeserializeAsync<Data>(await res.Content.ReadAsStreamAsync());
                    datasetId = data.DatasetId;
                }

                return datasetId;
            }

            static VehicleResponse GetCarAsync(int vehicleId, HttpClient client, string datasetId)
            {
                if (vehicleId <= 0)
                {
                    return null;
                }
                var response = client.GetAsync(datasetId + "/vehicles/" + vehicleId).Result;
                var carRes = response.Content.ReadAsStringAsync();
                var car = JsonConvert.DeserializeObject<VehicleResponse>(carRes.Result);
                return car;
            }

            static DealerResponse GetDealerAsync(int dealerId, HttpClient client, string datasetId)
            {
                if (dealerId <= 0)
                {
                    return null;
                }

                var response = client.GetAsync(datasetId + "/dealers/" + dealerId).Result;
                var res = response.Content.ReadAsStringAsync();
                var dealer = JsonConvert.DeserializeObject<DealerResponse>(res.Result);
                return dealer;
            }
        }
    }
}
