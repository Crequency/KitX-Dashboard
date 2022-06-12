using ReactiveUI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

#pragma warning disable CS0108 // ��Ա���ؼ̳еĳ�Ա��ȱ�ٹؼ��� new
#pragma warning disable CS8618 // ���˳����캯��ʱ������Ϊ null ���ֶα�������� null ֵ���뿼������Ϊ����Ϊ null��

namespace KitX_Dashboard.ViewModels
{
    public class ViewModelBase : ReactiveObject
    {

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                return true;
            }
            return false;
        }
    }
}

#pragma warning restore CS8618 // ���˳����캯��ʱ������Ϊ null ���ֶα�������� null ֵ���뿼������Ϊ����Ϊ null��
#pragma warning restore CS0108 // ��Ա���ؼ̳еĳ�Ա��ȱ�ٹؼ��� new
