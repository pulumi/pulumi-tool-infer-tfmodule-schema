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

Additional options
 - `--generate-override`: Generate a schema override file to be used in the `pulumi-terraform-module` repository (see below)
 - `--skip-strings`: Skip emitting outputs with the `string` type. This is useful if you want to avoid redundant outputs in the generated schema since the default is already `string`.

### Example usage with AWS VPC module

```
pulumi plugin run infer-tfmodule-schema terraform-aws-modules/vpc/aws 5.18.1 config.json
```

### Usage with local modules

```
pulumi plugin run infer-tfmodule-schema <local-module-path> <output-file-name>
```

### Using the generated schema

The generated JSON file can then be used with `pulumi-terraform-module` via the parameter `--config config.json` to enhance the inferred schema of the TF module. 

For example:

```
pulumi package add terraform-module -- <module-source> <version> <packageName> --config config.json
```

### Generating schema override to be used in the pulumi-terraform-module repository

Use the `--generate-override` option to generate a schema override file that can be used in the `pulumi-terraform-module` repository. The difference is that the generated file cannot be used directly as config but can be included in the `pulumi-terraform-module` repository to override the schema of the known module. 
```
pulumi plugin run infer-tfmodule-schema -- <module-source> <version> <output-file> --generate-override
```
After running the command, you can open a PR in the `pulumi-terraform-module` repository with the generated file inside the `./pkg/modprovider/module_schema_overrides` directory. The name of file should indicate which module it is for and the version should be the next major version of the module. For example, if the module is `terraform-aws-modules/vpc/aws` and the version is `5.18.1`, the file _can_ be named `terraform-aws-modules_vpc_aws_6_0_0.json`. The structure of the file name isn't required

Since the default inferred output type of the pulumi-terraform-module is `string`, you can skip emitting outputs with the `string` type using the `--skip-strings` option because the override would be redundant.

### Publishing a new version of the plugin

Bump the version field in the project file at `./src/InferModuleSchema.csproj` and push that change. When the commit lands to `master` branch, GitHub actions will check that the new version isn't published yet and publish it automatically to the releases, making it immediately available for the users.

If you want to retract a version, simply remove it from the releases and push to `master` again, GitHub actions will see that the version isn't publish and will publish accordingly. 

See the `./build/Build.fs` file to see how where this happens.
