using System.Text;

namespace Microsoft.Boogie.TPTP;

public class MultiTextWriter : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    private IEnumerable<TextWriter> writers;

    public MultiTextWriter(params TextWriter[] writers)
    {
        this.writers = writers;
    }

    private void ForEach(Action<TextWriter> action)
    {
        foreach (TextWriter wr in writers)
        {
            action.Invoke(wr);
        }
    }

    public override void Close()
    {
        ForEach((w) => w.Close());
        base.Close();
    }

    public override void Flush()
    {
        ForEach((w) => w.Flush());
        base.Flush();
    }

    public override void Write(char value)
    {
        ForEach((w) => w.Write(value));
        base.Write(value);
    }

    protected override void Dispose(bool disposing)
    {
        ForEach((w) => w.Dispose());
        base.Dispose(disposing);
    }


}