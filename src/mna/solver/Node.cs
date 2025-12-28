namespace Sparky.Mna.Solver;

public class Node {
    public int Id { get; }
    public double Voltage { get; set; }

    public Node(int id) {
        Id = id;
    }
}

