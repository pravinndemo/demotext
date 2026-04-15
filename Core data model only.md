<mxfile host="app.diagrams.net">
  <diagram name="Core Data Model">
    <mxGraphModel dx="1280" dy="720" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="1600" pageHeight="900" math="0" shadow="0">
      <root>
        <mxCell id="0"/>
        <mxCell id="1" parent="0"/>

        <mxCell id="app" value="Model-Driven App" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#dae8fc;strokeColor=#6c8ebf;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="60" y="60" width="180" height="60" as="geometry"/>
        </mxCell>

        <mxCell id="bulk" value="Bulk Processor&#xa;--------------------------------&#xa;Batch Id&#xa;Source Type&#xa;Status&#xa;Requested Job Type&#xa;Assignment Mode&#xa;Team / Manager&#xa;File Reference&#xa;Total / Valid / Failed Counts" style="rounded=0;whiteSpace=wrap;html=1;fillColor=#fff2cc;strokeColor=#d6b656;align=left;spacingLeft=8;" vertex="1" parent="1">
          <mxGeometry x="320" y="180" width="260" height="220" as="geometry"/>
        </mxCell>

        <mxCell id="item" value="Bulk Processor Item&#xa;--------------------------------&#xa;Bulk Processor Id&#xa;SSU Id / Hereditament Ref&#xa;Source Row Number&#xa;Validation Status&#xa;Validation Message&#xa;Request Id&#xa;Job Id&#xa;Attempt Count&#xa;Processed On" style="rounded=0;whiteSpace=wrap;html=1;fillColor=#f8cecc;strokeColor=#b85450;align=left;spacingLeft=8;" vertex="1" parent="1">
          <mxGeometry x="660" y="180" width="290" height="240" as="geometry"/>
        </mxCell>

        <mxCell id="request" value="Request" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#d5e8d4;strokeColor=#82b366;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="1040" y="170" width="140" height="60" as="geometry"/>
        </mxCell>

        <mxCell id="job" value="Job" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#d5e8d4;strokeColor=#82b366;fontStyle=1;" vertex="1" parent="1">
          <mxGeometry x="1040" y="300" width="140" height="60" as="geometry"/>
        </mxCell>

        <mxCell id="e1" value="manages batch" style="edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;" edge="1" parent="1" source="app" target="bulk">
          <mxGeometry relative="1" as="geometry"/>
        </mxCell>

        <mxCell id="e2" value="1 to many" style="edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;endArrow=block;" edge="1" parent="1" source="bulk" target="item">
          <mxGeometry relative="1" as="geometry"/>
        </mxCell>

        <mxCell id="e3" value="may create" style="edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;endArrow=block;" edge="1" parent="1" source="item" target="request">
          <mxGeometry relative="1" as="geometry"/>
        </mxCell>

        <mxCell id="e4" value="may create" style="edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;endArrow=block;" edge="1" parent="1" source="item" target="job">
          <mxGeometry relative="1" as="geometry"/>
        </mxCell>

        <mxCell id="e5" value="linked to" style="edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;endArrow=block;" edge="1" parent="1" source="job" target="request">
          <mxGeometry relative="1" as="geometry"/>
        </mxCell>
      </root>
    </mxGraphModel>
  </diagram>
</mxfile>
