using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 카드의 잇기 칸 — 위아래 방향 (Orientation.Vertical), 이름 없음, 칸 줄 폭만큼 (선을 놓기 쉽게 — 연결 점은 가운데). 놓기는 GraphView 기본 (Port.Create의 것)과 같은 차례이고,
/// 새로 끈 선을 빈 곳에 놓으면 droppedOutside (칸 · 놓은 자리 — 패널 좌표)를 부른다 — 그 자리에 새 노드를 만들어 잇게 (Unreal처럼).
/// </summary>
public sealed class BehaviorGraphPort : Port
{
    private BehaviorGraphPort(Direction direction, Capacity capacity)
        : base(Orientation.Vertical, direction, capacity, typeof(BehaviorMainNode))
    {
    }

    public static BehaviorGraphPort Create(Direction direction, Capacity capacity, Action<BehaviorGraphPort, Vector2> droppedOutside)
    {
        var port = new BehaviorGraphPort(direction, capacity);
        port.m_EdgeConnector = new EdgeConnector<Edge>(new Listener(port, droppedOutside));
        port.AddManipulator(port.m_EdgeConnector);
        port.portName = ""; // 형식 이름 대신 빈 글 — 글 칸은 남긴다 (칸을 누르는 범위가 글 칸 자리로 정해진다)
        port.style.flexGrow = 1; // 칸 줄 폭만큼 — 선을 놓는 자리는 칸의 사각형이다 (GraphView는 세로 칸의 놓는 자리를 넓히지 않는다)
        port.style.justifyContent = Justify.Center;
        return port;
    }

    /// <summary>끌어 놓기 — Port.Create의 기본 받기와 같다 (한 칸 포트의 다른 선을 먼저 지우고 graphViewChanged에 만들 선을 묻는다) + 빈 곳에 놓기</summary>
    private sealed class Listener : IEdgeConnectorListener
    {
        private readonly BehaviorGraphPort _port;
        private readonly Action<BehaviorGraphPort, Vector2> _droppedOutside;
        private readonly List<Edge> _edgesToCreate = new List<Edge>();
        private readonly List<GraphElement> _edgesToDelete = new List<GraphElement>();

        public Listener(BehaviorGraphPort port, Action<BehaviorGraphPort, Vector2> droppedOutside)
        {
            _port = port;
            _droppedOutside = droppedOutside;
        }

        /// <summary>빈 곳에 놓았다 — 새로 끈 선만 (있던 선을 끌어 놓으면 두 끝이 다 있다 — 그 선은 GraphView가 지운다)</summary>
        public void OnDropOutsidePort(Edge edge, Vector2 position)
        {
            if (edge.input != null && edge.output != null)
                return;
            _droppedOutside?.Invoke(_port, position);
        }

        public void OnDrop(GraphView graphView, Edge edge)
        {
            _edgesToCreate.Clear();
            _edgesToCreate.Add(edge);

            // 한 칸 포트에 이미 이은 선은 먼저 지운다 — DeleteElements가 graphViewChanged에 알린다
            _edgesToDelete.Clear();
            if (edge.input.capacity == Capacity.Single)
            {
                foreach (Edge other in edge.input.connections)
                {
                    if (other != edge)
                        _edgesToDelete.Add(other);
                }
            }
            if (edge.output.capacity == Capacity.Single)
            {
                foreach (Edge other in edge.output.connections)
                {
                    if (other != edge)
                        _edgesToDelete.Add(other);
                }
            }
            if (_edgesToDelete.Count > 0)
                graphView.DeleteElements(_edgesToDelete);

            List<Edge> edgesToCreate = _edgesToCreate;
            if (graphView.graphViewChanged != null)
                edgesToCreate = graphView.graphViewChanged(new GraphViewChange { edgesToCreate = _edgesToCreate }).edgesToCreate;

            foreach (Edge created in edgesToCreate)
            {
                graphView.AddElement(created);
                edge.input.Connect(created);
                edge.output.Connect(created);
            }
        }
    }
}