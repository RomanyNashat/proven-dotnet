#!/usr/bin/env python3
"""The handoff for the mobile team, from two OpenAPI documents: what changed in each endpoint, an example
request and response, the errors and headers, and what an app version already installed will see.

    python api_handoff.py main.json branch.json [--headers App-Version,x-language] [--out handoff.md]
                          [--json] [--fail-on-break]

Each change is judged for an installed app, by the rules in `api-contract-evolution`:
  breaks  - a new value in a response enum, a response field removed or retyped or now nullable,
            a new required request field or parameter, a request enum value removed, an endpoint removed;
  risky   - a response field removed that wasn't required (a blank where the value was), or now optional;
  safe    - a new optional field, a new endpoint, a new request enum value;
  note    - a new status code, a field the request no longer reads, an endpoint marked deprecated.
--headers names the headers every call already sends (the team's), listed once at the top.
Exit 0, or 1 with --fail-on-break when anything breaks.
"""
from __future__ import annotations

import argparse
import http
import json
import sys
from pathlib import Path

METHODS = ("get", "put", "post", "delete", "patch", "head", "options")
ORDER = {"breaks": 0, "risky": 1, "note": 2, "safe": 3}


class Doc:
    def __init__(self, data: dict):
        self.data = data

    def resolve(self, node, seen=()):
        while isinstance(node, dict) and "$ref" in node:
            ref = node["$ref"]
            if ref in seen or not ref.startswith("#/"):
                return {}
            seen = (*seen, ref)
            target = self.data
            for part in ref[2:].split("/"):
                target = target.get(part.replace("~1", "/").replace("~0", "~"), {}) if isinstance(target, dict) else {}
            node = target
        return node if isinstance(node, dict) else {}

    def schema(self, node, seen=()) -> dict:
        """A schema with its $ref followed and allOf merged; refs it passed through are in `_seen`."""
        refs = list(seen)
        while isinstance(node, dict) and "$ref" in node:
            if node["$ref"] in refs:
                return {"_cycle": True}
            refs.append(node["$ref"])
            node = self.resolve({"$ref": node["$ref"]})
        node = dict(node or {})
        for part in node.pop("allOf", []):
            sub = self.schema(part, tuple(refs))
            node.setdefault("properties", {}).update(sub.get("properties", {}))
            node["required"] = sorted(set(node.get("required", [])) | set(sub.get("required", [])))
            for key in ("type", "enum", "format"):
                if key in sub and key not in node:
                    node[key] = sub[key]
        node["_seen"] = tuple(refs)
        return node


def describe(s: dict) -> tuple[str, bool, tuple]:
    """(type, nullable, enum values) of a resolved schema, the 3.0 and 3.1 spellings alike."""
    types = s.get("type")
    types = [types] if isinstance(types, str) else list(types or [])
    nullable = bool(s.get("nullable")) or "null" in types
    for alt in s.get("oneOf", []) + s.get("anyOf", []):
        if isinstance(alt, dict) and alt.get("type") == "null":
            nullable = True
        elif isinstance(alt, dict) and alt.get("type") and not types:
            types = [alt["type"]] if isinstance(alt["type"], str) else alt["type"]
    types = [t for t in types if t != "null"]
    kind = (types[0] if types else ("object" if "properties" in s else "any"))
    if s.get("format"):
        kind += f" ({s['format']})"
    enum = tuple(v for v in s.get("enum", []) if v is not None)
    if None in s.get("enum", []):
        nullable = True
    return kind, nullable, enum


def flatten(doc: Doc, node, path="", required=True, depth=0, out=None) -> dict:
    """Every field of a body by its path (`status`, `items[].room`): its type, nullable, enum, required."""
    out = {} if out is None else out
    s = doc.schema(node)
    if s.get("_cycle") or depth > 6:
        return out
    alts = [a for a in s.get("oneOf", []) + s.get("anyOf", []) if isinstance(a, dict) and a.get("type") != "null"]
    if len(alts) == 1 and not s.get("type") and "properties" not in s:
        s = {**doc.schema(alts[0], s["_seen"]), "nullable": True if describe(s)[1] else s.get("nullable")}
    kind, nullable, enum = describe(s)
    if path:
        out[path] = {"type": kind, "nullable": nullable, "enum": enum, "required": required}
    if "properties" in s:
        needed = set(s.get("required", []))
        for name, prop in s["properties"].items():
            flatten(doc, prop, f"{path}.{name}" if path else name, name in needed, depth + 1, out)
    if "array" in kind and "items" in s:
        flatten(doc, s["items"], f"{path}[]", True, depth + 1, out)
    return out


def example(doc: Doc, node, depth=0):
    s = doc.schema(node)
    for key in ("example", "default"):
        if key in s:
            return s[key]
    if s.get("examples"):
        return s["examples"][0] if isinstance(s["examples"], list) else next(iter(s["examples"].values()), None)
    kind, nullable, enum = describe(s)
    if s.get("_cycle") or depth > 4:
        return None
    if enum:
        return enum[0]
    if "properties" in s:
        return {k: example(doc, v, depth + 1) for k, v in s["properties"].items()}
    if kind.startswith("array"):
        return [example(doc, s.get("items", {}), depth + 1)]
    return {"integer": 0, "number": 0, "boolean": False}.get(kind.split(" ")[0], {
        "string (date)": "2026-01-31", "string (date-time)": "2026-01-31T09:30:00Z", "string (uuid)": "0190a5b2-7c1d-7000-8000-000000000000",
    }.get(kind, "string" if kind.startswith("string") else None))


def operations(doc: Doc) -> dict:
    ops = {}
    for path, item in (doc.data.get("paths") or {}).items():
        item = doc.resolve(item)
        shared = item.get("parameters", [])
        for method in METHODS:
            if method in item:
                op = doc.resolve(item[method])
                params = {}
                for p in shared + op.get("parameters", []):
                    p = doc.resolve(p)
                    params[(p.get("in"), p.get("name"))] = p
                ops[f"{method.upper()} {path}"] = {"op": op, "params": params}
    return ops


def json_schema(doc: Doc, holder: dict):
    content = doc.resolve(holder).get("content") or {}
    for media, body in content.items():
        if "json" in media:
            return media, body.get("schema", {})
    return (next(iter(content)), {}) if content else (None, None)


def body_fields(doc: Doc, op: dict):
    rb = doc.resolve(op.get("requestBody", {}))
    media, schema = json_schema(doc, rb) if rb else (None, None)
    return (flatten(doc, schema) if schema is not None else {}), bool(rb.get("required")), schema


def responses(doc: Doc, op: dict) -> dict:
    return {str(code): doc.resolve(r) for code, r in (op.get("responses") or {}).items()}


def compare_fields(old: dict, new: dict, where: str, request: bool) -> list[dict]:
    changes = []
    add = lambda field, what, impact, why: changes.append({"where": where, "field": field, "what": what, "impact": impact, "why": why})
    for f in sorted(set(new) - set(old)):
        if request and new[f]["required"] and parent_present(f, old):
            add(f, "new required field", "breaks", f"installed apps don't send `{f}`, so every request they make is rejected (400)")
        else:
            add(f, "new field" + (" (required)" if new[f]["required"] else ""), "safe",
                "installed apps don't send it" if request else "installed apps ignore it")
    for f in sorted(set(old) - set(new)):
        if request:
            add(f, "removed", "note", f"installed apps still send `{f}`: the server must ignore it, not reject it")
        elif old[f]["required"]:
            add(f, "removed", "breaks", f"an app whose model requires `{f}` can't read the response at all")
        else:
            add(f, "removed", "risky", f"installed apps show a blank where `{f}` was")
    for f in sorted(set(old) & set(new)):
        a, b = old[f], new[f]
        if a["type"] != b["type"]:
            add(f, f"type {a['type']} → {b['type']}", "breaks",
                f"installed apps {'send' if request else 'read'} `{f}` as {a['type']}")
        added, removed = [v for v in b["enum"] if v not in a["enum"]], [v for v in a["enum"] if v not in b["enum"]]
        if a["enum"] and added:
            if request:
                add(f, f"enum adds {', '.join(map(str, added))}", "safe", "installed apps never send it")
            else:
                add(f, f"enum adds {', '.join(map(str, added))}", "breaks",
                    f"an installed app that reads `{f}` as an enum can't read the response at all; send "
                    f"{', '.join(map(str, added))} only to app versions that know it, and an existing value to older ones")
        if removed:
            if request:
                add(f, f"enum drops {', '.join(map(str, removed))}", "breaks", f"installed apps still send {', '.join(map(str, removed))}")
            else:
                add(f, f"enum drops {', '.join(map(str, removed))}", "safe", "installed apps just never receive it")
        if b["nullable"] and not a["nullable"] and not request:
            add(f, "can now be null", "breaks", f"an app whose model says `{f}` is never null fails to read a null")
        if a["nullable"] and not b["nullable"] and request:
            add(f, "can't be null any more", "breaks", f"installed apps may still send null for `{f}`")
        if request and b["required"] and not a["required"]:
            add(f, "now required", "breaks", f"installed apps that leave out `{f}` are rejected (400)")
        if not request and a["required"] and not b["required"]:
            add(f, "now optional", "risky", f"`{f}` may be missing; installed apps expect it")
    return changes


def parent_present(field: str, old: dict) -> bool:
    """A required field inside a new optional object doesn't break anyone: only a new field of a part
    installed apps already send does."""
    parent = field.rsplit(".", 1)[0] if "." in field else ""
    parent = parent[:-2] if parent.endswith("[]") else parent
    return parent == "" or parent in old


def compare_operation(old_doc: Doc, new_doc: Doc, old: dict, new: dict) -> list[dict]:
    changes = []
    add = lambda where, field, what, impact, why: changes.append({"where": where, "field": field, "what": what, "impact": impact, "why": why})
    for key in sorted(set(new["params"]) | set(old["params"]), key=str):
        where, name = key
        a, b = old["params"].get(key), new["params"].get(key)
        label = f"{where} parameter"
        if b and not a:
            if b.get("required"):
                add(label, name, "new required parameter", "breaks", f"installed apps don't send `{name}`")
            else:
                add(label, name, "new optional parameter", "safe", "installed apps don't send it")
        elif a and not b:
            add(label, name, "removed", "note", f"installed apps still send `{name}`")
        else:
            ta, tb = describe(old_doc.schema(a.get("schema", {})))[0], describe(new_doc.schema(b.get("schema", {})))[0]
            if ta != tb:
                add(label, name, f"type {ta} → {tb}", "breaks", f"installed apps send `{name}` as {ta}")
            if b.get("required") and not a.get("required"):
                add(label, name, "now required", "breaks", f"installed apps that leave out `{name}` are rejected")
    old_body, old_req, _ = body_fields(old_doc, old["op"])
    new_body, new_req, _ = body_fields(new_doc, new["op"])
    changes += compare_fields(old_body, new_body, "request body", True)
    if new_req and not old_req and not old_body:
        add("request body", "", "body now required", "breaks", "installed apps send no body")
    old_res, new_res = responses(old_doc, old["op"]), responses(new_doc, new["op"])
    for code in sorted(set(old_res) | set(new_res)):
        if code in new_res and code not in old_res:
            add(f"response {code}", "", "new status code", "note", f"installed apps have never seen a {code}: they show their generic error")
        elif code in old_res and code not in new_res:
            add(f"response {code}", "", "no longer documented", "note", "check the app doesn't wait for it")
        elif code.startswith("2"):
            a = flatten(old_doc, json_schema(old_doc, old_res[code])[1] or {})
            b = flatten(new_doc, json_schema(new_doc, new_res[code])[1] or {})
            changes += compare_fields(a, b, f"response {code}", False)
    if new["op"].get("deprecated") and not old["op"].get("deprecated"):
        add("endpoint", "", "deprecated", "note", "installed apps keep calling it: keep it working until its sunset")
    return changes


def build(old_doc: Doc, new_doc: Doc) -> dict:
    old_ops, new_ops = operations(old_doc), operations(new_doc)
    endpoints = []
    for key in sorted(set(old_ops) | set(new_ops), key=lambda k: (k.split(" ", 1)[1], k)):
        if key in new_ops and key not in old_ops:
            changes = [{"where": "endpoint", "field": "", "what": "new", "impact": "safe", "why": "only app versions built for it call it"}]
            status = "new"
        elif key in old_ops and key not in new_ops:
            changes = [{"where": "endpoint", "field": "", "what": "removed", "impact": "breaks", "why": "installed apps still call it and get a 404"}]
            status = "removed"
        else:
            changes = compare_operation(old_doc, new_doc, old_ops[key], new_ops[key])
            status = "changed"
            if not changes:
                continue
        changes.sort(key=lambda c: (ORDER[c["impact"]], c["where"], c["field"]))
        doc, ops = (new_doc, new_ops) if key in new_ops else (old_doc, old_ops)
        endpoints.append({"endpoint": key, "status": status, "impact": changes[0]["impact"], "changes": changes,
                          **details(doc, ops[key])})
    endpoints.sort(key=lambda e: (ORDER[e["impact"]], {"removed": 0, "changed": 1, "new": 2}[e["status"]], e["endpoint"]))
    info = lambda d: f"{d.data.get('info', {}).get('title', 'API')} {d.data.get('info', {}).get('version', '')}".strip()
    return {"from": info(old_doc), "to": info(new_doc), "endpoints": endpoints}


def details(doc: Doc, entry: dict) -> dict:
    op = entry["op"]
    _, _, schema = body_fields(doc, op)
    res = responses(doc, op)
    ok = next((c for c in sorted(res) if c.startswith("2")), None)
    ok_schema = json_schema(doc, res[ok])[1] if ok else None
    errors = []
    for code in sorted(c for c in res if c[:1] in "45"):
        media, _ = json_schema(doc, res[code])
        shape = "ProblemDetails (type, title, status, detail)" if media == "application/problem+json" else (media or "no body")
        description = res[code].get("description", "")
        try:
            if description.strip().lower() == http.HTTPStatus(int(code)).phrase.lower():
                description = ""  # "Not Found" under a 404 says nothing new
        except ValueError:
            pass
        errors.append({"status": code, "body": shape, "description": description})
    headers = [{"name": name, "required": bool(p.get("required"))} for (where, name), p in entry["params"].items() if where == "header"]
    response_headers = sorted({h for r in res.values() for h in (r.get("headers") or {})})
    return {"request": example(doc, schema) if schema else None, "ok": ok,
            "response": example(doc, ok_schema) if ok_schema else None, "errors": errors,
            "headers": headers, "response_headers": response_headers}


LABEL = {"breaks": "breaks installed apps", "risky": "risky for installed apps", "note": "check", "safe": "safe"}


def markdown(h: dict, team_headers: list[str]) -> str:
    eps = h["endpoints"]
    breaking = [e for e in eps if e["impact"] == "breaks"]
    out = [f"# API changes for the mobile team: {h['from']} → {h['to']}", ""]
    if not eps:
        return "\n".join(out + ["Nothing an app calls has changed.", ""])
    lead = f"{len(eps)} endpoint{'s' if len(eps) != 1 else ''} changed"
    lead += f"; {len(breaking)} break{'s' if len(breaking) == 1 else ''} app versions already installed." if breaking else \
        "; none breaks an app version already installed."
    out += [lead, ""]
    if team_headers:
        out += [f"Every call sends {', '.join(f'`{x}`' for x in team_headers)}.", ""]
    for e in eps:
        out += [f"## {e['endpoint']} ({e['status']}, {LABEL[e['impact']]})", ""]
        bad = [c for c in e["changes"] if c["impact"] in ("breaks", "risky")]
        if bad:
            out += ["**What an installed app sees:**", ""]
            out += [f"- {c['why'][0].upper() + c['why'][1:]}." for c in bad]
            out.append("")
        if e["status"] == "changed":
            out += ["| Change | Where | Installed apps |", "|---|---|---|"]
            out += [f"| {('`' + c['field'] + '` ') if c['field'] else ''}{c['what']} | {c['where']} | {LABEL[c['impact']]} |" for c in e["changes"]]
            out.append("")
        if e["status"] != "removed":
            if e["headers"]:
                out += ["**Headers:** " + ", ".join(f"`{x['name']}`" + (" (required)" if x["required"] else "") for x in e["headers"]), ""]
            if e["request"] is not None:
                out += ["**Request:**", "", "```json", json.dumps(e["request"], ensure_ascii=False, indent=2), "```", ""]
            if e["ok"]:
                out += [f"**Response {e['ok']}:**", ""]
                out += (["```json", json.dumps(e["response"], ensure_ascii=False, indent=2), "```", ""] if e["response"] is not None else ["No body.", ""])
            if e["response_headers"]:
                out += ["**Response headers:** " + ", ".join(f"`{x}`" for x in e["response_headers"]), ""]
            if e["errors"]:
                out += ["**Errors:**", ""] + [f"- {x['status']}: {x['body']}" + (f". {x['description']}" if x["description"] else "") for x in e["errors"]] + [""]
    out.append("Examples are built from the schema where the document has none.")
    return "\n".join(out) + "\n"


def read_text(path: Path) -> str:
    raw = path.read_bytes()
    if raw[:2] in (b"\xff\xfe", b"\xfe\xff"):  # Windows PowerShell's `>` writes UTF-16
        return raw.decode("utf-16")
    return raw.decode("utf-8-sig")


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("old", type=Path)
    ap.add_argument("new", type=Path)
    ap.add_argument("--headers", default="")
    ap.add_argument("--out", type=Path)
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--fail-on-break", action="store_true")
    args = ap.parse_args()
    docs = []
    for p in (args.old, args.new):
        try:
            docs.append(Doc(json.loads(read_text(p))))
        except (OSError, ValueError) as e:
            print(f"{p}: can't read it as JSON ({e.__class__.__name__}). YAML: convert it first, or generate the JSON document.")
            return 2
    h = build(*docs)
    if h["from"] == h["to"]:  # both documents say the same title and version: name them by their files
        h["from"], h["to"] = args.old.name, args.new.name
    text = json.dumps(h, ensure_ascii=False, indent=2) if args.json else markdown(h, [x.strip() for x in args.headers.split(",") if x.strip()])
    if args.out:
        args.out.write_text(text, encoding="utf-8")
        print(f"{args.out}: {len(h['endpoints'])} endpoints, {sum(e['impact'] == 'breaks' for e in h['endpoints'])} breaking")
    else:
        print(text, end="")
    breaks = any(e["impact"] == "breaks" for e in h["endpoints"])
    return 1 if args.fail_on_break and breaks else 0


if __name__ == "__main__":
    sys.exit(main())
