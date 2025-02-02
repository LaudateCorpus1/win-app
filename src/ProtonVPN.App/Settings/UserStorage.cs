﻿/*
 * Copyright (c) 2021 Proton Technologies AG
 *
 * This file is part of ProtonVPN.
 *
 * ProtonVPN is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * ProtonVPN is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with ProtonVPN.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Globalization;
using System.Security.Cryptography;
using ProtonVPN.Common.Extensions;
using ProtonVPN.Common.Logging;
using ProtonVPN.Core.Api.Contracts;
using ProtonVPN.Core.Models;
using ProtonVPN.Core.OS.Crypto;
using ProtonVPN.Core.Servers;
using ProtonVPN.Core.Settings;
using ProtonVPN.Core.Storage;
using ProtonVPN.Core.User;
using UserLocation = ProtonVPN.Core.User.UserLocation;

namespace ProtonVPN.Settings
{
    internal class UserStorage : IUserStorage
    {
        private const string FREE_VPN_PLAN = "free";

        private readonly ILogger _logger;
        private readonly ISettingsStorage _storage;
        private readonly UserSettings _userSettings;

        public event EventHandler UserDataChanged;
        public event EventHandler<VpnPlanChangedEventArgs> VpnPlanChanged;

        public UserStorage(
            ILogger logger,
            ISettingsStorage storage,
            UserSettings userSettings)
        {
            _logger = logger;
            _storage = storage;
            _userSettings = userSettings;
        }

        public void SaveUsername(string username)
        {
            _storage.Set("Username", username.Encrypt());
        }

        public void SetFreePlan()
        {
            string oldVpnPlan = _userSettings.Get<string>("VpnPlan");

            _userSettings.Set("VpnPlan", FREE_VPN_PLAN);
            _userSettings.Set("ExpirationTime", 0);
            _userSettings.Set("MaxTier", ServerTiers.Free);
            
            VpnPlanChangedEventArgs eventArgs = new VpnPlanChangedEventArgs(oldVpnPlan, FREE_VPN_PLAN);
            VpnPlanChanged?.Invoke(this, eventArgs);
            UserDataChanged?.Invoke(this, EventArgs.Empty);
        }

        public User User()
        {
            try
            {
                return UnsafeUser();
            }
            catch (CryptographicException e)
            {
                _logger.Error(e);
            }

            return Core.Models.User.EmptyUser();
        }

        public void SaveLocation(UserLocation location)
        {
            _storage.Set("Ip", location.Ip.Encrypt());
            _storage.Set("Country", location.Country.Encrypt());
            _storage.Set("Isp", location.Isp.Encrypt());
            _storage.Set("Latitude", location.Latitude.ToString(CultureInfo.InvariantCulture).Encrypt());
            _storage.Set("Longitude", location.Longitude.ToString(CultureInfo.InvariantCulture).Encrypt());
        }

        public UserLocation Location()
        {
            try
            {
                return UnsafeLocation();
            }
            catch (CryptographicException ex)
            {
                _logger.Error(ex);
            }

            return UserLocation.Empty;
        }

        public void ClearLogin()
        {
            _storage.Set("Username", "");
        }

        public void StoreVpnInfo(VpnInfoResponse vpnInfo)
        {
            int expirationTime = vpnInfo.Vpn.ExpirationTime;
            sbyte maxTier = vpnInfo.Vpn.MaxTier;
            string vpnPlan = vpnInfo.Vpn.PlanName;

            if (Core.Models.User.IsDelinquent(vpnInfo.Delinquent))
            {
                expirationTime = 0;
                maxTier = ServerTiers.Free;
                vpnPlan = FREE_VPN_PLAN;
            }

            CacheUser(new User
            {
                ExpirationTime = expirationTime,
                MaxTier = maxTier,
                Services = vpnInfo.Services,
                VpnPlan = vpnPlan,
                VpnPassword = vpnInfo.Vpn.Password,
                VpnUsername = vpnInfo.Vpn.Name,
                Delinquent = vpnInfo.Delinquent,
                MaxConnect = vpnInfo.Vpn.MaxConnect,
                OriginalVpnPlan = vpnInfo.Vpn.PlanName
            });
        }

        private User UnsafeUser()
        {
            string username = _storage.Get<string>("Username")?.Trim();
            if (string.IsNullOrEmpty(username))
            {
                return Core.Models.User.EmptyUser();
            }

            username = username.Decrypt();

            string vpnUsername = _userSettings.Get<string>("VpnUsername");
            if (!string.IsNullOrEmpty(vpnUsername))
            {
                vpnUsername = vpnUsername.Decrypt();
            }

            string vpnPassword = _userSettings.Get<string>("VpnPassword");
            if (!string.IsNullOrEmpty(vpnPassword))
            {
                vpnPassword = vpnPassword.Decrypt();
            }

            int delinquent = _userSettings.Get<int>("Delinquent");
            string originalVpnPlan = _userSettings.Get<string>("VpnPlan");
            string vpnPlan = originalVpnPlan;
            if (Core.Models.User.IsDelinquent(delinquent))
            {
                vpnPlan = FREE_VPN_PLAN;
            }

            return new User
            {
                Username = username,
                VpnPlan = vpnPlan,
                MaxTier = _userSettings.Get<sbyte>("MaxTier"),
                Delinquent = delinquent,
                ExpirationTime = _userSettings.Get<int>("ExpirationTime"),
                MaxConnect = _userSettings.Get<int>("MaxConnect"),
                Services = _userSettings.Get<int>("Services"),
                VpnUsername = vpnUsername,
                VpnPassword = vpnPassword,
                OriginalVpnPlan = originalVpnPlan
            };
        }

        public UserLocation UnsafeLocation()
        {
            string ip = _storage.Get<string>("Ip")?.Trim();
            string latitude = _storage.Get<string>("Latitude")?.Trim();
            string longitude = _storage.Get<string>("Longitude")?.Trim();
            string isp = _storage.Get<string>("Isp");
            string country = _storage.Get<string>("Country");

            if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(latitude) || string.IsNullOrEmpty(longitude))
            {
                return UserLocation.Empty;
            }

            float latitudeFloat = float.Parse(latitude.Decrypt(), CultureInfo.InvariantCulture.NumberFormat);
            float longitudeFloat = float.Parse(longitude.Decrypt(), CultureInfo.InvariantCulture.NumberFormat);
            return new UserLocation(ip.Decrypt(), latitudeFloat, longitudeFloat, isp.Decrypt(), country.Decrypt());
        }

        private void SaveUserData(User user)
        {
            _userSettings.Set("VpnPlan", user.OriginalVpnPlan);
            _userSettings.Set("MaxTier", user.MaxTier);
            _userSettings.Set("Delinquent", user.Delinquent);
            _userSettings.Set("ExpirationTime", user.ExpirationTime);
            _userSettings.Set("MaxConnect", user.MaxConnect);
            _userSettings.Set("Services", user.Services);
            _userSettings.Set("VpnUsername", !string.IsNullOrEmpty(user.VpnUsername) ? user.VpnUsername.Encrypt() : string.Empty);
            _userSettings.Set("VpnPassword", !string.IsNullOrEmpty(user.VpnPassword) ? user.VpnPassword.Encrypt() : string.Empty);
        }

        private void CacheUser(User user)
        {
            User previousData = User();
            SaveUserData(user);

            if (!previousData.VpnPlan.IsNullOrEmpty() && previousData.VpnPlan != user.VpnPlan)
            {
                VpnPlanChangedEventArgs eventArgs = new VpnPlanChangedEventArgs(previousData.VpnPlan, user.VpnPlan);
                VpnPlanChanged?.Invoke(this, eventArgs);
            }

            UserDataChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}