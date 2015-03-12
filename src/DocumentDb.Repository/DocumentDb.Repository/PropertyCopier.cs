using System.Reflection;

namespace DocumentDB.Repository
{
    public static class PropertyCopier
    {
        public static void CopyProperties<TSource, TTarget>(this TSource source, TTarget destination, bool copyIdOnly = false)
        {
            PropertyInfo[] destinationProperties = destination.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var destinationPi in destinationProperties)
            {
                if (!destinationPi.CanWrite)
                    continue;

                if (destinationPi.Name.ToLower().Equals("id"))
                {
                    if (copyIdOnly)
                    {
                        CopyValue(source, destination, destinationPi);
                        break;
                    }

                    continue;
                }

                CopyValue(source, destination, destinationPi);
            }
        }

        private static void CopyValue<TSource, TTarget>(TSource source, TTarget destination, PropertyInfo destinationPi)
        {
            PropertyInfo sourcePi = source.GetType().GetProperty(destinationPi.Name);

            if (sourcePi != null)
            {
                if (!sourcePi.CanRead)
                    return;

                destinationPi.SetValue(destination, sourcePi.GetValue(source, null), null);
            }
        }
    }
}
