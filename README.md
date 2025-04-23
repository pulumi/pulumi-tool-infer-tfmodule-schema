# infer-tfmodule-schema

A tool for inferring the schema of TF modules, specifically the output types of the modules to be used as extra configuration with the [pulumi-terraform-module](https://github.com/pulumi/pulumi-terraform-module) provider. 

It uses OpenAI `gpt-4o` to analyze the module and generate a JSON schema for the output variables. As such, you will need an OpenAI API key to use this tool and it is expected to be set in the environment variable `OPENAI_KEY`.

### Installation

```
pulumi plugin install tool infer-tfmodule-schema
```


### Usage with remote modules

```
pulumi plugin run infer-tfmodule-schema -- <module-source> <version> <output-file-name>
```

### Example usage with AWS VPC module

```
pulumi plugin run infer-tfmodule-schema terraform-aws-modules/vpc/aws 5.18.1 config.json
```

### Usage with local modules

```
pulumi run infer-tfmodule-schema <local-module-path> <output-file-name>
```

### Using the generated schema

The generated JSON file can then be used with `pulumi-terraform-module` via the parameter `--config config.json` to enhance the inferred schema of the TF module. 

For example:

```
pulumi package add terraform-module -- <module-source> <version> <packageName> --config config.json
```
