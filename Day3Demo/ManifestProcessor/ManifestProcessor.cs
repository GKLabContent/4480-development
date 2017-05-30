using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckSmartManifestTypes;

namespace ManifestProcessor
{
    public class ManifestProcessor
    {
        public static ManifestResponse ProcessMessage(ManifestRequest datagram)
        {

            var earthRadiusKm = 6371;

            var dLat = degreesToRadians(datagram.End.Latitude - datagram.Start.Latitude);
            var dLon = degreesToRadians(datagram.End.Longitude - datagram.Start.Longitude);

            var lat1 = degreesToRadians(datagram.End.Latitude);
            var lat2 = degreesToRadians(datagram.Start.Latitude);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(lat1) * Math.Cos(lat2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            var distance = earthRadiusKm * c;
            var output = new ManifestResponse() { ServerAddress = datagram.ServerAddress, ClientAddress = datagram.ClientAddress, Distance = distance, Status = distance < 1000 ? ManifestStatus.Accepted : ManifestStatus.Pending };
            return output;
        }
        /// <summary>
        /// Converts a value from degrees to radians.  This is a support function for ProcessMessage
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        private static double degreesToRadians(double p)
        {
            return p * Math.PI / 180.0;

        }
    }
}
