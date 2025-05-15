using System;
using System.Collections.Generic;

public class PriorityQueue<T>
{
    private readonly List<(T item, float priority)> elements = new List<(T, float)>();
    private readonly IComparer<T> comparer;

    public PriorityQueue(IComparer<T> comparer)
    {
        this.comparer = comparer;
    }

    public void Enqueue(T item)
    {
        elements.Add((item, elements.Count));
        BubbleUp(elements.Count - 1);
    }

    public T Dequeue()
    {
        if (elements.Count == 0)
            throw new InvalidOperationException("Queue is empty.");

        var result = elements[0].item;
        elements[0] = elements[elements.Count - 1];
        elements.RemoveAt(elements.Count - 1);
        if (elements.Count > 0)
            BubbleDown(0);
        return result;
    }

    public int Count => elements.Count;

    private void BubbleUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (comparer.Compare(elements[index].item, elements[parent].item) >= 0)
                break;
            Swap(index, parent);
            index = parent;
        }
    }

    private void BubbleDown(int index)
    {
        while (true)
        {
            int leftChild = 2 * index + 1;
            int rightChild = 2 * index + 2;
            int smallest = index;

            if (leftChild < elements.Count && comparer.Compare(elements[leftChild].item, elements[smallest].item) < 0)
                smallest = leftChild;
            if (rightChild < elements.Count && comparer.Compare(elements[rightChild].item, elements[smallest].item) < 0)
                smallest = rightChild;

            if (smallest == index)
                break;

            Swap(index, smallest);
            index = smallest;
        }
    }

    private void Swap(int i, int j)
    {
        var temp = elements[i];
        elements[i] = elements[j];
        elements[j] = temp;
    }
}