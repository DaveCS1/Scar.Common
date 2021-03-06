using System;
using System.Collections.Generic;
using Scar.Common.DAL.Model;

namespace Scar.Common.DAL
{
    public interface ITrackedRepository
    {
        ICollection<ITrackedEntity> GetModifiedAfter(DateTime startExclusive);

        ICollection<ITrackedEntity> GetModifiedBefore(DateTime endExclusive);

        ICollection<ITrackedEntity> GetModifiedBetween(DateTime startInclusive, DateTime endExclusive);

        ICollection<ITrackedEntity> GetCreatedAfter(DateTime startExclusive);

        ICollection<ITrackedEntity> GetCreatedBefore(DateTime endExclusive);

        ICollection<ITrackedEntity> GetCreatedBetween(DateTime startInclusive, DateTime endExclusive);
    }
}