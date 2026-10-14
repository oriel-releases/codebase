import SwiftUI
import OrielKit

struct InterestsView: View {
    @Environment(AppModel.self) private var model
    @State private var newTopic = ""

    var body: some View {
        @Bindable var model = model
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                Text("Oriel picks each photo from the interests you turn on.")
                    .foregroundStyle(.secondary)

                section("Interests", topics: model.settings.topics.indices.filter { !model.settings.topics[$0].isCustom })

                VStack(alignment: .leading, spacing: 12) {
                    section("Your own", topics: model.settings.topics.indices.filter { model.settings.topics[$0].isCustom })
                    HStack {
                        TextField("Add your own, like “tokyo night” or “foggy forest”", text: $newTopic)
                            .textFieldStyle(.roundedBorder)
                            .onSubmit(add)
                        Button("Add", action: add)
                            .disabled(newTopic.trimmingCharacters(in: .whitespaces).isEmpty)
                    }
                    .frame(maxWidth: 520)
                }

                if model.settings.enabledQueries().isEmpty {
                    Banner(icon: "info.circle", text: "Nothing is on, so Oriel picks landscapes.")
                }
            }
            .padding(24)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    @ViewBuilder
    private func section(_ title: String, topics: [Int]) -> some View {
        @Bindable var model = model
        VStack(alignment: .leading, spacing: 10) {
            Text(title).font(.headline)
            FlowLayout(spacing: 8) {
                ForEach(topics, id: \.self) { i in
                    let topic = model.settings.topics[i]
                    Toggle(topic.query.capitalized, isOn: $model.settings.topics[i].enabled)
                        .toggleStyle(.button)
                        .buttonBorderShape(.capsule)
                        .contextMenu {
                            if topic.isCustom {
                                Button("Remove", role: .destructive) { model.settings.topics.remove(at: i) }
                            }
                        }
                }
            }
        }
    }

    private func add() {
        let query = newTopic.trimmingCharacters(in: .whitespaces)
        guard !query.isEmpty else { return }
        if let i = model.settings.topics.firstIndex(where: { $0.query.caseInsensitiveCompare(query) == .orderedSame }) {
            model.settings.topics[i].enabled = true
        } else {
            model.settings.topics.append(TopicSetting(query: query, enabled: true, isCustom: true))
        }
        newTopic = ""
    }
}

/// Lays children out left to right, wrapping to new lines like words.
struct FlowLayout: Layout {
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let rows = arrange(width: proposal.width ?? .infinity, subviews)
        return CGSize(width: proposal.width ?? rows.map(\.width).max() ?? 0,
                      height: rows.last.map { $0.y + $0.height } ?? 0)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        for row in arrange(width: bounds.width, subviews) {
            var x = bounds.minX
            for i in row.items {
                let size = subviews[i].sizeThatFits(.unspecified)
                subviews[i].place(at: CGPoint(x: x, y: bounds.minY + row.y), proposal: .unspecified)
                x += size.width + spacing
            }
        }
    }

    private struct Row { var items: [Int] = []; var y: CGFloat = 0; var width: CGFloat = 0; var height: CGFloat = 0 }

    private func arrange(width: CGFloat, _ subviews: Subviews) -> [Row] {
        var rows: [Row] = []
        var row = Row()
        for (i, view) in subviews.enumerated() {
            let size = view.sizeThatFits(.unspecified)
            if !row.items.isEmpty && row.width + spacing + size.width > width {
                rows.append(row)
                row = Row(y: row.y + row.height + spacing)
            }
            row.width += (row.items.isEmpty ? 0 : spacing) + size.width
            row.height = max(row.height, size.height)
            row.items.append(i)
        }
        if !row.items.isEmpty { rows.append(row) }
        return rows
    }
}
