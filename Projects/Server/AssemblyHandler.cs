/*************************************************************************
 * ModernUO                                                              *
 * Copyright (C) 2019 - ModernUO Development Team                        *
 * Email: hi@modernuo.com                                                *
 * File: AssemblyHandler.cs - Created: 2019/08/02 - Updated: 2020/01/19  *
 *                                                                       *
 * This program is free software: you can redistribute it and/or modify  *
 * it under the terms of the GNU General Public License as published by  *
 * the Free Software Foundation, either version 3 of the License, or     *
 * (at your option) any later version.                                   *
 *                                                                       *
 * This program is distributed in the hope that it will be useful,       *
 * but WITHOUT ANY WARRANTY; without even the implied warranty of        *
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the         *
 * GNU General Public License for more details.                          *
 *                                                                       *
 * You should have received a copy of the GNU General Public License     *
 * along with this program.  If not, see <http://www.gnu.org/licenses/>. *
 *************************************************************************/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Collections.Immutable;

namespace Server
{
  public static class AssemblyHandler
  {
    private static Dictionary<Assembly, TypeCache> m_TypeCaches = new Dictionary<Assembly, TypeCache>();
    private static TypeCache m_NullCache;
    public static Assembly[] Assemblies { get; set; }

    public static string AssembliesPath = EnsureDirectory("Assemblies");

    public static void LoadScripts(string path = null) =>
      Assemblies = Directory.GetFiles(path ?? AssembliesPath, "*.dll")
        .Select(t => AssemblyLoadContext.Default.LoadFromAssemblyPath(t)).ToArray();

    public static void Invoke(string method)
    {
      List<MethodInfo> invoke = new List<MethodInfo>();

      for (int a = 0; a < Assemblies.Length; ++a)
        invoke.AddRange(Assemblies[a].GetTypes()
          .Select(t => t.GetMethod(method, BindingFlags.Static | BindingFlags.Public)).Where(m => m != null));

      invoke.Sort(new CallPriorityComparer());

      for (int i = 0; i < invoke.Count; ++i)
        invoke[i].Invoke(null, null);
    }

    public static TypeCache GetTypeCache(Assembly asm)
    {
      if (asm == null) return m_NullCache ??= new TypeCache(null);

      if (m_TypeCaches.TryGetValue(asm, out TypeCache c))
        return c;

      return m_TypeCaches[asm] = new TypeCache(asm);
    }

    public static Type FindFirstTypeForName(string name, bool ignoreCase = false, Func<Type, bool> predicate = null)
    {
      var types = FindTypesByName(name, ignoreCase).ToList();
      if (types.Count == 0)
        return null;
      if (predicate != null)
        return types.FirstOrDefault(predicate);
      if (types.Count == 1)
        return types[0];
      // Try to find the closest match if there is no predicate.
      // Check for exact match of the FullName or Name
      // Then check for case-insensitive match of FullName or Name
      // Otherwise just return the first entry
      return (!ignoreCase ? types.FirstOrDefault(x => x.FullName == name || x.Name == name) : null)
        ?? types.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.FullName, name) || StringComparer.OrdinalIgnoreCase.Equals(x.Name, name))
        ?? types[0];
    }
    public static IEnumerable<Type> FindTypesByName(string name, bool ignoreCase = false)
    {
      List<Type> types = new List<Type>();
      if(ignoreCase)
        name = name.ToLower();
      for (int i = 0; i < Assemblies.Length; i++)
      {
        types.AddRange(GetTypeCache(Assemblies[i])[name]);
      }
      if (types.Count == 0)
        types.AddRange(GetTypeCache(Core.Assembly)[name]);
      return types;
    }

    public static string EnsureDirectory(string dir)
    {
      string path = Path.Combine(Core.BaseDirectory, dir);

      if (!Directory.Exists(path))
        Directory.CreateDirectory(path);

      return path;
    }
  }

  public class TypeCache
  {
    private Dictionary<string, int[]> m_NameMap = new Dictionary<string, int[]>();
    private Type[] m_Types;
    public IEnumerable<Type> Types { get => m_Types; }
    public IEnumerable<string> Names { get => m_NameMap.Keys; }

    public IEnumerable<Type> this[string name]
    {
      get => m_NameMap.TryGetValue(name, out int[] value) ? value.Select(x => m_Types[x]) : new Type[0];
    }

    public TypeCache(Assembly asm)
    {
      m_Types = asm?.GetTypes() ?? Type.EmptyTypes;
      var nameMap = new Dictionary<string, HashSet<int>>();
      HashSet<int> refs;
      Action<int, string> addToRefs = (index, key) =>
      {
        if (nameMap.TryGetValue(key, out refs))
          refs.Add(index);
        else
        {
          refs = new HashSet<int>();
          refs.Add(index);
          nameMap.Add(key, refs);
        }
      };
      Type current;
      Type aliasType = typeof(TypeAliasAttribute);
      TypeAliasAttribute alias;
      for (int i = 0, j = 0; i < m_Types.Length; i++)
      {
        current = m_Types[i];
        addToRefs(i, current.Name);
        addToRefs(i, current.Name.ToLower());
        addToRefs(i, current.FullName);
        addToRefs(i, current.FullName.ToLower());
        alias = current.GetCustomAttribute(aliasType, false) as TypeAliasAttribute;
        if (alias != null)
          for (j = 0; j < alias.Aliases.Length; j++)
          {
            addToRefs(i, alias.Aliases[j]);
            addToRefs(i, alias.Aliases[j].ToLower());
          }
      }
      foreach (var entry in nameMap)
        m_NameMap[entry.Key] = entry.Value.ToArray();
    }
  }
}
