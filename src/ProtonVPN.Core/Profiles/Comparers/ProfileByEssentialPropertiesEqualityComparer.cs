﻿/*
 * Copyright (c) 2020 Proton Technologies AG
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

using ProtonVPN.Common.Helpers;
using System.Collections.Generic;

namespace ProtonVPN.Core.Profiles.Comparers
{
    /// <summary>
    /// Compares only essential properties send to Profiles API. ExternalId and ColorCode is not included.
    /// </summary>
    public class ProfileByEssentialPropertiesEqualityComparer : IEqualityComparer<Profile>
    {
        public bool Equals(Profile x, Profile y)
        {
            if (ReferenceEquals(null, x)) return false;
            if (ReferenceEquals(null, y)) return false;
            if (ReferenceEquals(x, y)) return true;

            return x.ProfileType == y.ProfileType
                   && x.Name == y.Name
                   && x.VpnProtocol == y.VpnProtocol
                   && (x.CountryCode ?? "") == (y.CountryCode ?? "")
                   && (x.ServerId ?? "") == (y.ServerId ?? "")
                   && x.Features == y.Features;
        }

        public int GetHashCode(Profile obj)
        {
            return HashCode.Start
                .Hash(obj.ProfileType)
                .Hash(obj.Name)
                .Hash(obj.VpnProtocol)
                .Hash(obj.CountryCode ?? "")
                .Hash(obj.ServerId ?? "")
                .Hash(obj.Features);
        }
    }
}
