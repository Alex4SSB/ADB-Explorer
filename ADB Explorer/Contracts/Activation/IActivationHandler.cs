using System.Threading.Tasks;

namespace ADB_Explorer.Contracts.Activation
{
    public interface IActivationHandler
    {
        bool CanHandle();

        Task HandleAsync();
    }
}
