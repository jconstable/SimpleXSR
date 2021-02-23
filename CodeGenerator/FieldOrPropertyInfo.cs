using System;
using System.Reflection;

namespace SimpleCrossSceneReferences
{
    public class FieldOrPropertyInfo
    {
        public readonly MemberInfo MemberInfo;

        public FieldOrPropertyInfo(MemberInfo memberInfo)
        {
            MemberInfo = memberInfo;
        }

        public Type GetDerrivedType()
        {
            switch (MemberInfo.MemberType)
            {
                case MemberTypes.Property:
                    return ((PropertyInfo)MemberInfo).PropertyType;
                case MemberTypes.Field:
                    return ((FieldInfo)MemberInfo).FieldType;
                default:
                    throw new NotImplementedException();
            }
        }

        public object GetValue(object o)
        {
            switch (MemberInfo.MemberType)
            {
                case MemberTypes.Property:
                    return ((PropertyInfo)MemberInfo).GetValue(o);
                case MemberTypes.Field:
                    return ((FieldInfo)MemberInfo).GetValue(o);
                default:
                    throw new NotImplementedException();
            }
        }

        public void SetValue(object o, object value)
        {
            switch (MemberInfo.MemberType)
            {
                case MemberTypes.Property:
                    ((PropertyInfo)MemberInfo).SetValue(o, value);
                    break;
                case MemberTypes.Field:
                    ((FieldInfo)MemberInfo).SetValue(o, value);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public bool IsPublic()
        {
            switch (MemberInfo.MemberType)
            {
                case MemberTypes.Property:
                    return ((PropertyInfo)MemberInfo).GetMethod.IsPublic && ((PropertyInfo)MemberInfo).SetMethod.IsPublic;
                case MemberTypes.Field:
                    return ((FieldInfo)MemberInfo).IsPublic;
                default:
                    throw new NotImplementedException();
            }
        }
    }
}