using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Newtonsoft.Json;
using TruckSmartManifestTypes;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.File;
using Microsoft.Azure;

namespace ProcessManifests
{
    public class Functions
    {
        // This function will get triggered/executed when a new message is written 
        // on an Azure Queue called queue.
        public static void ProcessQueueMessage([QueueTrigger("manifests")] string message, TextWriter log)
        {
            var datagram = JsonConvert.DeserializeObject<ManifestRequest>(message);

            #region Calculate Distance

            var earthRadiusKm = 6371;

            var dLat = degreesToRadians(datagram.End.Latitude - datagram.Start.Latitude);
            var dLon = degreesToRadians(datagram.End.Longitude - datagram.Start.Longitude);

            var lat1 = degreesToRadians(datagram.End.Latitude);
            var lat2 = degreesToRadians(datagram.Start.Latitude);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(lat1) * Math.Cos(lat2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            var distance = earthRadiusKm * c;
            #endregion

            var output = new ManifestResponse() { ServerAddress = System.Environment.MachineName, ClientAddress = datagram.ClientAddress, Distance = distance, Status = distance < 1000 ? ManifestStatus.Accepted : ManifestStatus.Pending };


            #region Store datagram and result
            string fileNameBase = string.Format("{0}@{1:yyyyMMdd.HHmmss}", datagram.ShipmentID, DateTime.Now);
            string inputFileName = string.Format("{0}.datagram.json", fileNameBase);
            string outputFileName = string.Format("{0}.result.json", fileNameBase);
            string datagramContents = JsonConvert.SerializeObject(datagram);
            string outputContents = JsonConvert.SerializeObject(output);

            var acct = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("storageAccount"));
            var client = acct.CreateCloudFileClient();
            var share = client.GetShareReference("manifests");
            share.CreateIfNotExists();

            var root = share.GetRootDirectoryReference();
            var datagrams = root.GetDirectoryReference("datagrams");
            var results = root.GetDirectoryReference("results");
            datagrams.CreateIfNotExists();
            results.CreateIfNotExists();


            var datagramFile = datagrams.GetFileReference(inputFileName);
            var outputFile = results.GetFileReference(outputFileName);
            datagramFile.UploadText(datagramContents);
            outputFile.UploadText(outputContents);
            #endregion

            log.WriteLine("Processed: {0}", fileNameBase);

        }

        private static double degreesToRadians(double p)
        {
            return p * Math.PI / 180.0;

        }
    }
}
