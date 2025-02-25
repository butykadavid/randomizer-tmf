﻿using RandomizerTMF.Logic.Services;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using TmEssentials;
using YamlDotNet.Serialization;

namespace RandomizerTMF.Logic;

public class RequestRules
{
    private static readonly ESite[] siteValues = Enum.GetValues<ESite>();
    private static readonly EEnvironment[] envValues = Enum.GetValues<EEnvironment>();
    private static readonly EEnvironment[] sunriseEnvValues = new [] { EEnvironment.Island, EEnvironment.Bay, EEnvironment.Coast };
    private static readonly EEnvironment[] originalEnvValues = new [] { EEnvironment.Desert, EEnvironment.Snow, EEnvironment.Rally };

    // Custom rules that are not part of the official API

    public required ESite Site { get; set; }
    public bool EqualEnvironmentDistribution { get; set; }
    public bool EqualVehicleDistribution { get; set; }

    public string? Author { get; set; }
    public HashSet<EEnvironment>? Environment { get; set; }
    public string? Name { get; set; }
    public HashSet<EEnvironment>? Vehicle { get; set; }
    public EPrimaryType? PrimaryType { get; set; }
    public ETag? Tag { get; set; }
    public HashSet<EMood>? Mood { get; set; }
    public HashSet<EDifficulty>? Difficulty { get; set; }
    public HashSet<ERoutes>? Routes { get; set; }
    public ELbType? LbType { get; set; }
    public bool? InBeta { get; set; }
    public bool? InPlayLater { get; set; }
    public bool? InFeatured { get; set; }
    public bool? InSupporter { get; set; }
    public bool? InFavorite { get; set; }
    public bool? InDownloads { get; set; }
    public bool? InReplays { get; set; }
    public bool? InEnvmix { get; set; }
    public bool? InHasRecord { get; set; }
    public bool? InLatestAuthor { get; set; }
    public bool? InLatestAwardedAuthor { get; set; }
    public bool? InScreenshot { get; set; }
    public DateOnly? UploadedBefore { get; set; }
    public DateOnly? UploadedAfter { get; set; }
    public bool SurvivalMode { get; set; }
    public TimeSpan? SurvivalBonusTime { get; set; }
    public TimeInt32? AuthorTimeMin { get; set; }
    public TimeInt32? AuthorTimeMax { get; set; }
    public int? FreeSkipLimit { get; set; }
    public int? GoldSkipLimit { get; set; }

    public string ToUrl(IRandomGenerator random) // Not very efficient but does the job done fast enough
    {
        var b = new StringBuilder("https://");

        var matchingSites = siteValues
            .Where(x => x != ESite.Any && (Site & x) == x)
            .ToArray();

        // If Site is Any, then it picks from sites that are valid within environments and cars
        var site = GetRandomSite(random, matchingSites.Length == 0
            ? siteValues.Where(x => x is not ESite.Any
            && IsSiteValidWithEnvironments(x)
            && IsSiteValidWithVehicles(x)
            && IsSiteValidWithEnvimix(x)).ToArray()
            : matchingSites);

        var siteUrl = GetSiteUrl(site);

        b.Append(siteUrl);
        
        b.Append("/trackrandom");

        var first = true;

        foreach (var prop in GetType().GetProperties().Where(IsQueryProperty))
        {
            var val = prop.GetValue(this);

            if (EqualEnvironmentDistribution && prop.Name == nameof(Environment))
            {
                val = GetRandomEnvironmentThroughSet(random, Environment, site);
            }

            if (EqualVehicleDistribution && prop.Name == nameof(Vehicle))
            {
                val = GetRandomEnvironmentThroughSet(random, Vehicle, site);
            }

            if (val is null || (val is IEnumerable enumerable && !enumerable.Cast<object>().Any()))
            {
                continue;
            }

            // Adjust url on weird combinations
            if (site is ESite.TMNF or ESite.Nations && !IsValidInNations(prop, val))
            {
                continue;
            }

            if (first)
            {
                b.Append('?');
                first = false;
            }
            else
            {
                b.Append('&');
            }

            b.Append(prop.Name.ToLower());
            b.Append('=');

            var genericType = prop.PropertyType.IsGenericType ? prop.PropertyType.GetGenericTypeDefinition() : null;

            if (genericType == typeof(Nullable<>))
            {
                AppendValue(b, prop.PropertyType.GetGenericArguments()[0], val, genericType);
            }
            else
            {
                AppendValue(b, prop.PropertyType, val, genericType);
            }
        }

        return b.ToString();
    }

    private bool IsQueryProperty(PropertyInfo prop)
    {
        return prop.Name is not nameof(Site)
                        and not nameof(EqualEnvironmentDistribution)
                        and not nameof(EqualVehicleDistribution);
    }

    private static bool IsSiteValidWithEnvironments(ESite site, HashSet<EEnvironment>? envs)
    {
        if (envs is null)
        {
            return true;
        }
        
        return site switch
        {
            ESite.Sunrise => envs.Contains(EEnvironment.Island) || envs.Contains(EEnvironment.Coast) || envs.Contains(EEnvironment.Bay),
            ESite.Original => envs.Contains(EEnvironment.Snow) || envs.Contains(EEnvironment.Desert) || envs.Contains(EEnvironment.Rally),
            ESite.TMNF or ESite.Nations => envs.Contains(EEnvironment.Stadium),
            _ => true,
        };
    }

    private bool IsSiteValidWithEnvironments(ESite site)
    {
        return IsSiteValidWithEnvironments(site, Environment);
    }

    private bool IsSiteValidWithVehicles(ESite site)
    {
        return IsSiteValidWithEnvironments(site, Vehicle);
    }

    private bool IsSiteValidWithEnvimix(ESite site)
    {
        if (site is not ESite.Sunrise and not ESite.Original || Environment is null || Environment.Count == 0)
        {
            return true;
        }

        foreach (var env in Environment)
        {
            if (Vehicle?.Contains(env) == false)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsValidInNations(PropertyInfo prop, object val)
    {
        if (prop.Name is nameof(Environment) or nameof(Vehicle) && !val.Equals(EEnvironment.Stadium))
        {
            return false;
        }

        if (prop.Name is nameof(PrimaryType) && !val.Equals(EPrimaryType.Race))
        {
            return false;
        }

        return true;
    }

    private static EEnvironment GetRandomEnvironment(IRandomGenerator random, HashSet<EEnvironment>? container, ESite site)
    {
        if (container is not null && container.Count != 0)
        {
            return container.ElementAt(random.Next(container.Count));
        }
        
        return site switch
        {
            ESite.Sunrise => sunriseEnvValues[random.Next(sunriseEnvValues.Length)],
            ESite.Original => originalEnvValues[random.Next(originalEnvValues.Length)],
            _ => (EEnvironment)random.Next(envValues.Length) // Safe in case of EEnvironment
        };
    }

    private static HashSet<EEnvironment> GetRandomEnvironmentThroughSet(IRandomGenerator random, HashSet<EEnvironment>? container, ESite site)
    {
        return new HashSet<EEnvironment>() { GetRandomEnvironment(random, container, site) };
    }

    private static ESite GetRandomSite(IRandomGenerator random, ESite[] matchingSites)
    {
        return matchingSites[random.Next(matchingSites.Length)];
    }

    private static string GetSiteUrl(ESite site) => site switch
    {
        ESite.Any => throw new UnreachableException("Any is not a valid site"),
        ESite.TMNF => "tmnf.exchange",
        ESite.TMUF => "tmuf.exchange",
        _ => $"{site.ToString().ToLower()}.tm-exchange.com",
    };

    private static void AppendValue(StringBuilder b, Type type, object val, Type? genericType = null)
    {
        if (val is TimeInt32 timeInt32)
        {
            b.Append(timeInt32.TotalMilliseconds);
        }
        else if (val is bool boolVal)
        {
            b.Append(boolVal ? '1' : '0');
        }
        else if (val is DateOnly date)
        {
            b.Append(date.ToString("yyyy-MM-dd"));
        }
        else if (genericType == typeof(HashSet<>))
        {
            var elementType = type.GetGenericArguments()[0] ?? throw new UnreachableException("Array has null element type");

            var first = true;

            foreach (var elem in (IEnumerable)val)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    b.Append("%2C");
                }

                AppendValue(b, elementType, elem);
            }
            
        }
        else if (type.IsEnum)
        {
            b.Append((int)val);
        }
        else
        {
            b.Append(val);
        }
    }
}